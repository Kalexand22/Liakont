namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.Documents.Contracts;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Implémentation de <see cref="IDocumentSendActions"/>. Réutilise <b>à l'identique</b> l'orchestration des
/// endpoints API02a (<c>DocumentActionsEndpointMapping</c> : <c>POST /documents/{id}/send</c> et
/// <c>/send-all</c>) et runs/trigger (<c>PipelineEndpointMapping</c> : <c>POST /runs/trigger</c>) — mêmes
/// gardes d'état (lecture tenant-scopée), même publication du déclencheur mono-tenant <see cref="SendTenantTrigger"/>
/// sur la queue SYSTÈME (ADR-0016), mêmes codes d'audit (via les SOURCES UNIQUES
/// <see cref="DocumentActionContract"/> / <see cref="PipelineRunActionContract"/>) et même identité d'opérateur.
/// La console appelle ce service in-process depuis son circuit serveur (le cookie OIDC n'est pas disponible pour
/// boucler sur l'endpoint HTTP, précédent WEB03b). Aucune logique fiscale ni machine à états n'est dupliquée et
/// AUCUN second chemin d'envoi n'est ouvert : l'envoi proprement dit reste dans le pipeline. TENANT-SCOPÉ par
/// construction (la connexion EST le tenant).
/// </summary>
internal sealed class DocumentSendActionsService : IDocumentSendActions
{
    /// <summary>Taille de page des lectures par état (file bornée — même surface que l'endpoint send-all).</summary>
    private const int PageSize = 100;

    /// <summary>Profondeur de lecture du journal pour retrouver le run déclenché (FIX05) : le run cherché vient
    /// d'être lancé, il est donc en tête (tri début décroissant) — une petite fenêtre suffit largement.</summary>
    private const int RunLookupLimit = 20;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly IDocumentQueries _documents;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IActorContextAccessor _actorAccessor;
    private readonly IActivityLogger _activityLogger;
    private readonly IPermissionService _permissions;
    private readonly IPipelineRunQueries _runQueries;
    private readonly TimeProvider _timeProvider;
    private readonly SendRunWaitPolicy _waitPolicy;

    /// <summary>
    /// Constructeur de PRODUCTION (résolu par le conteneur). <see cref="TimeProvider"/> n'est pas enregistré dans
    /// le Host (idiome du repo : valeur par défaut <see cref="TimeProvider.System"/>) ; la politique d'attente du
    /// résultat de run utilise <see cref="SendRunWaitPolicy.Default"/>. Le conteneur ne voit QUE ce constructeur
    /// public — le constructeur <c>internal</c> (horloge + politique injectables) est réservé aux tests.
    /// </summary>
    public DocumentSendActionsService(
        IDocumentQueries documents,
        IServiceScopeFactory scopeFactory,
        IActorContextAccessor actorAccessor,
        IActivityLogger activityLogger,
        IPermissionService permissions,
        IPipelineRunQueries runQueries)
        : this(documents, scopeFactory, actorAccessor, activityLogger, permissions, runQueries, TimeProvider.System, SendRunWaitPolicy.Default)
    {
    }

    /// <summary>Constructeur testable : horloge et politique d'attente du résultat de run injectées (FIX05).</summary>
    internal DocumentSendActionsService(
        IDocumentQueries documents,
        IServiceScopeFactory scopeFactory,
        IActorContextAccessor actorAccessor,
        IActivityLogger activityLogger,
        IPermissionService permissions,
        IPipelineRunQueries runQueries,
        TimeProvider timeProvider,
        SendRunWaitPolicy waitPolicy)
    {
        _documents = documents;
        _scopeFactory = scopeFactory;
        _actorAccessor = actorAccessor;
        _activityLogger = activityLogger;
        _permissions = permissions;
        _runQueries = runQueries;
        _timeProvider = timeProvider;
        _waitPolicy = waitPolicy;
    }

    public async Task<DocumentSendActionResult> SendSelectionAsync(
        IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken = default)
    {
        if (DenyIfNotAuthorized() is { } denied)
        {
            return denied;
        }

        if (documentIds is null || documentIds.Count == 0)
        {
            return DocumentSendActionResult.Failure("Sélectionnez au moins un document à envoyer.");
        }

        var actor = _actorAccessor.Current;
        if (string.IsNullOrWhiteSpace(actor.TenantId))
        {
            return DocumentSendActionResult.Failure("Tenant non résolu : action d'envoi impossible.");
        }

        // Validation par document (mêmes gardes que POST /documents/{id}/send) : seuls les ReadyToSend partent.
        var ready = new List<(Guid Id, string Number)>();
        var skipped = new List<string>();
        foreach (var id in documentIds)
        {
            var document = await _documents.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                skipped.Add(string.Create(Fr, $"document {id} introuvable dans ce tenant"));
                continue;
            }

            if (!string.Equals(document.State, DocumentActionContract.ReadyToSendState, StringComparison.Ordinal))
            {
                skipped.Add(string.Create(Fr, $"le document n° {document.DocumentNumber} n'est pas prêt à l'envoi (état : {document.State})"));
                continue;
            }

            ready.Add((document.Id, document.DocumentNumber));
        }

        if (ready.Count == 0)
        {
            return DocumentSendActionResult.Failure(
                "Aucun document prêt à l'envoi dans la sélection : " + DescribeSkipped(skipped));
        }

        // ADR-0016 : le traitement d'envoi du tenant émet TOUS les ReadyToSend (il boucle sur l'état, pas sur
        // l'id) — un SEUL déclencheur suffit pour la sélection (publier un job par document serait redondant).
        // Chaque document prêt est néanmoins journalisé (parité d'audit avec POST /documents/{id}/send).
        var jobId = await PublishTenantSendAsync(actor, cancellationToken).ConfigureAwait(false);

        var operatorId = ActorId(actor);
        foreach (var (id, number) in ready)
        {
            await _activityLogger.LogActivityAsync(
                DocumentActionContract.DocumentEntityType,
                id.ToString(),
                DocumentActionContract.SendTriggeredActivity,
                string.Create(CultureInfo.InvariantCulture, $"Envoi déclenché par l'opérateur depuis le document {number} : le traitement d'envoi du tenant émet tous les documents prêts à l'envoi (ce document inclus)."),
                operatorId,
                metadata: new { jobId, DocumentNumber = number },
                companyId: actor.CompanyId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var message = string.Create(Fr, $"Envoi déclenché : le traitement d'envoi du tenant émet TOUS les documents prêts à l'envoi (dont les {ready.Count} document(s) sélectionné(s)).");
        if (skipped.Count > 0)
        {
            message += " Ignoré(s) : " + DescribeSkipped(skipped);
        }

        return DocumentSendActionResult.Ok(message);
    }

    public async Task<DocumentSendSummary> SummarizeReadyToSendAsync(CancellationToken cancellationToken = default)
    {
        var count = 0;
        var totalGross = 0m;
        var page = 1;
        while (true)
        {
            var batch = await _documents
                .GetByStateAsync(DocumentActionContract.ReadyToSendState, page, PageSize, cancellationToken)
                .ConfigureAwait(false);
            foreach (var summary in batch)
            {
                count++;
                totalGross += summary.TotalGross;
            }

            if (batch.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return new DocumentSendSummary(count, totalGross);
    }

    public async Task<DocumentSendActionResult> SendAllAsync(CancellationToken cancellationToken = default)
    {
        if (DenyIfNotAuthorized() is { } denied)
        {
            return denied;
        }

        var actor = _actorAccessor.Current;
        if (string.IsNullOrWhiteSpace(actor.TenantId))
        {
            return DocumentSendActionResult.Failure("Tenant non résolu : action d'envoi impossible.");
        }

        var summary = await SummarizeReadyToSendAsync(cancellationToken).ConfigureAwait(false);
        if (summary.Count == 0)
        {
            return DocumentSendActionResult.Failure("Aucun document prêt à l'envoi : il n'y a rien à envoyer.");
        }

        var jobId = await PublishTenantSendAsync(actor, cancellationToken).ConfigureAwait(false);

        await _activityLogger.LogActivityAsync(
            DocumentActionContract.SendAllEntityType,
            DocumentActionContract.SendAllEntityId,
            DocumentActionContract.SendAllTriggeredActivity,
            string.Create(CultureInfo.InvariantCulture, $"Envoi groupé déclenché par l'opérateur : {summary.Count} document(s) prêt(s), montant total {summary.TotalGross.ToString("0.00", CultureInfo.InvariantCulture)}."),
            ActorId(actor),
            metadata: new { jobId, summary.Count, summary.TotalGross },
            companyId: actor.CompanyId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return DocumentSendActionResult.Ok(string.Create(Fr, $"Envoi groupé déclenché : {summary.Count} document(s) prêt(s), montant total {summary.TotalGross.ToString("N2", Fr)} € (le traitement d'envoi du tenant les émettra)."));
    }

    public async Task<DocumentSendActionResult> TriggerRunAsync(CancellationToken cancellationToken = default)
    {
        if (DenyIfNotAuthorized() is { } denied)
        {
            return denied;
        }

        var actor = _actorAccessor.Current;
        if (string.IsNullOrWhiteSpace(actor.TenantId))
        {
            return DocumentSendActionResult.Failure("Tenant non résolu : déclenchement impossible.");
        }

        // Horodatage AVANT publication : le run d'envoi qui en résulte démarrera APRÈS cet instant (même
        // horloge serveur), ce qui sert de borne basse pour le corréler dans le journal (FIX05).
        var triggeredAtUtc = _timeProvider.GetUtcNow();
        var jobId = await PublishTenantSendAsync(actor, cancellationToken).ConfigureAwait(false);

        await _activityLogger.LogActivityAsync(
            PipelineRunActionContract.RunEntityType,
            PipelineRunActionContract.RunEntityId,
            PipelineRunActionContract.RunTriggeredActivity,
            "Traitement déclenché manuellement par l'opérateur.",
            ActorId(actor),
            metadata: new { jobId, dryRun = false },
            companyId: actor.CompanyId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // FIX05 : ne PAS renvoyer un « déclenché » statique (qui ressemble à un succès même quand rien n'est
        // parti). On sonde brièvement le journal des exécutions jusqu'à ce que CE run d'envoi soit clôturé, puis
        // on remonte son résultat réel (émis / partiel / rien + motif). Au-delà du budget, dégradation gracieuse.
        return await AwaitTriggeredRunOutcomeAsync(triggeredAtUtc, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Projette le résultat d'un run d'envoi clôturé en message opérateur français (FIX05, CLAUDE.md n°12). Un run
    /// qui n'émet RIEN n'est jamais présenté comme un succès : il est signalé (<c>Success == false</c>) avec le
    /// MOTIF rédigé par le pipeline (« aucun compte Plateforme Agréée actif… », action corrective incluse).
    /// </summary>
    internal static DocumentSendActionResult DescribeSendRunOutcome(PipelineRunLogDto run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var emitted = run.DocumentsSucceeded;
        var failed = run.DocumentsFailed;
        var motif = string.IsNullOrWhiteSpace(run.Detail) ? null : run.Detail.Trim();

        if (emitted > 0 && failed == 0)
        {
            return DocumentSendActionResult.Ok(string.Create(Fr, $"Traitement terminé : {emitted} document(s) émis."));
        }

        if (emitted > 0 && failed > 0)
        {
            return DocumentSendActionResult.Ok(string.Create(Fr, $"Traitement terminé : {emitted} document(s) émis, {failed} en échec — consultez les documents en échec."));
        }

        // Aucun document émis : ce n'est PAS un succès silencieux. On expose le motif (paramétrage manquant,
        // SIREN non publié, rien de prêt…) pour que l'opérateur sache quoi faire.
        var head = failed > 0
            ? string.Create(Fr, $"Traitement terminé : aucun document émis, {failed} en échec.")
            : "Traitement terminé : aucun document émis.";
        return DocumentSendActionResult.Failure(motif is null ? head : head + " " + motif);
    }

    /// <summary>Identité d'audit de l'opérateur (GUID utilisateur ; « system » si non authentifié) — identique aux endpoints.</summary>
    private static string ActorId(IActorContext actor) =>
        actor.IsAuthenticated ? actor.UserId.ToString() : "system";

    /// <summary>Agrège les motifs d'exclusion en une phrase opérateur (numéro de document inclus — CLAUDE.md n°12).</summary>
    private static string DescribeSkipped(List<string> skipped) =>
        skipped.Count == 0 ? string.Empty : string.Join(" ; ", skipped) + ".";

    /// <summary>
    /// Attend (de façon bornée) la clôture du run d'envoi MANUEL déclenché à <paramref name="triggeredAtUtc"/> et
    /// renvoie son résultat opérateur (FIX05). Le run est identifié dans <c>pipeline.run_logs</c> (tenant-scopé)
    /// comme la dernière exécution <see cref="PipelineRunType.Send"/> / <see cref="PipelineRunTrigger.Manual"/>
    /// CLÔTURÉE dont le début est ≥ l'instant du déclenchement. Sonde au plus <c>MaxAttempts</c> fois ; si le run
    /// n'est pas encore clôturé (worker lent), renvoie un message neutre renvoyant au journal — jamais un faux succès.
    /// </summary>
    private async Task<DocumentSendActionResult> AwaitTriggeredRunOutcomeAsync(
        DateTimeOffset triggeredAtUtc, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _waitPolicy.MaxAttempts; attempt++)
        {
            var run = await FindCompletedManualSendRunAsync(triggeredAtUtc, cancellationToken).ConfigureAwait(false);
            if (run is not null)
            {
                return DescribeSendRunOutcome(run);
            }

            if (attempt < _waitPolicy.MaxAttempts)
            {
                await Task.Delay(_waitPolicy.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        // Dégradation gracieuse : le déclenchement a réussi, mais le run n'est pas encore clôturé. On ne prétend
        // PAS qu'il a abouti — on renvoie l'opérateur au journal des traitements (où le résultat ET le motif
        // apparaîtront, colonne Détail visible — FIX05).
        return DocumentSendActionResult.Ok(
            "Traitement lancé. Son résultat (documents émis ou motif d'absence d'envoi) apparaîtra dans le journal des traitements dès la fin de l'exécution.");
    }

    /// <summary>
    /// Dernière exécution SEND manuelle CLÔTURÉE dont le début est ≥ <paramref name="triggeredAtUtc"/>, ou
    /// <c>null</c> tant qu'aucune n'est clôturée. Le journal est rendu le plus récent en tête (lecture tenant-scopée).
    /// </summary>
    private async Task<PipelineRunLogDto?> FindCompletedManualSendRunAsync(
        DateTimeOffset triggeredAtUtc, CancellationToken cancellationToken)
    {
        var runs = await _runQueries.GetRecentRunsAsync(RunLookupLimit, cancellationToken).ConfigureAwait(false);
        return runs.FirstOrDefault(r =>
            r.RunType == PipelineRunType.Send
            && r.Trigger == PipelineRunTrigger.Manual
            && r.CompletedAt is not null
            && r.StartedAt >= triggeredAtUtc);
    }

    /// <summary>
    /// Publie le déclencheur d'envoi MONO-TENANT (<see cref="SendTenantTrigger"/>) sur la queue SYSTÈME
    /// (ADR-0016, exactement comme les endpoints) : un scope null-tenant FRAIS route <see cref="IJobQueue"/>
    /// vers la base SYSTÈME — jamais la base du tenant courant (où le job serait orphelin, jamais consommé par
    /// le <c>JobWorker</c> null-tenant). Le tenant cible voyage dans la charge utile.
    /// </summary>
    private async Task<Guid> PublishTenantSendAsync(IActorContext actor, CancellationToken cancellationToken)
    {
        await using var systemScope = _scopeFactory.CreateAsyncScope();
        var queue = systemScope.ServiceProvider.GetRequiredService<IJobQueue>();
        return await queue
            .EnqueueAsync(new SendTenantTrigger(actor.TenantId!, DryRun: false), companyId: actor.CompanyId, ct: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Refuse l'action si l'opérateur ne porte pas <c>liakont.actions</c> (défense en profondeur : les endpoints
    /// HTTP portent la même garde via <c>RequireAuthorization</c> ; le chemin in-process de la console ne doit pas
    /// dépendre du seul masquage des boutons côté UI). Renvoie <c>null</c> quand l'action est autorisée.
    /// </summary>
    private DocumentSendActionResult? DenyIfNotAuthorized() =>
        _permissions.HasPermission(LiakontPermissions.Actions)
            ? null
            : DocumentSendActionResult.Failure("Action non autorisée : la permission « actions » (liakont.actions) est requise.");
}

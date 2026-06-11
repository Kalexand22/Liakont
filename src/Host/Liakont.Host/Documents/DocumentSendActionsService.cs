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

    /// <summary>Message neutre renvoyant au journal (résultat de run indéterminé ou non corrélable — FIX05). Le
    /// wording explique le MODÈLE d'envoi (traitement du tenant, asynchrone, qui émet tout ce qui est prêt — FIX202,
    /// décision E) pour que l'opérateur ne re-clique pas en boucle en croyant que rien ne se passe.</summary>
    private static readonly DocumentSendActionResult JournalFallback = DocumentSendActionResult.Ok(
        "Le traitement d'envoi du tenant a été lancé ; il émet tous les documents prêts à l'envoi. Son résultat (documents émis ou motif d'absence d'envoi) apparaîtra dans le journal des traitements dès la fin de l'exécution.");

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
        // Horodatage AVANT publication : borne basse de corrélation du run déclenché (FIX05/FIX202).
        var triggeredAtUtc = _timeProvider.GetUtcNow();
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

        // FIX202 : ne PAS renvoyer un « envoi déclenché » statique (qui répète le même bandeau et laisse l'opérateur
        // re-cliquer en boucle sans jamais savoir si quelque chose est parti). On attend le résultat du run et on le
        // restitue ICI (émis / partiel / aucun envoi + motif), exactement comme « Lancer un traitement » (FIX05) —
        // les documents écartés à la sélection sont restitués À CÔTÉ du résultat, pas à sa place.
        var outcome = await AwaitTriggeredRunOutcomeAsync(triggeredAtUtc, cancellationToken).ConfigureAwait(false);
        return skipped.Count > 0
            ? outcome.WithSuffix("Ignoré(s) à la sélection : " + DescribeSkipped(skipped))
            : outcome;
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

        // Horodatage AVANT publication : borne basse de corrélation du run déclenché (FIX05/FIX202).
        var triggeredAtUtc = _timeProvider.GetUtcNow();
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

        // FIX202 : attendre la clôture du run et restituer son résultat réel (émis / aucun envoi + motif), au lieu
        // d'un « envoi groupé déclenché » statique. Le récapitulatif (nombre + montant) a déjà été présenté à la
        // confirmation et reste journalisé ci-dessus ; le bandeau de retour porte désormais le RÉSULTAT (FIX05).
        return await AwaitTriggeredRunOutcomeAsync(triggeredAtUtc, cancellationToken).ConfigureAwait(false);
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

        // Horodatage AVANT publication : sert de borne basse pour retrouver, dans le journal, le run d'envoi qui
        // en résulte (il démarrera après cet instant). En mono-nœud (déploiement V1, un seul Host), le worker
        // partage l'horloge du Host ; en multi-nœud, une dérive worker < host peut empêcher la corrélation — le
        // mode d'échec reste GRACIEUX (message neutre renvoyant au journal), jamais un faux résultat (FIX05).
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

        // Documents pris en compte mais ni émis ni en échec : différés (contenu pas encore stagé) ou ignorés
        // (SendTally — Processed = émis + échec + différés + ignorés). Sans cette mention, un run « 3 émis » qui
        // diffère 2 documents passerait pour intégralement envoyé (le lot n'est pas clos — FIX05, review P2).
        var pending = Math.Max(0, run.DocumentsProcessed - emitted - failed);
        var motif = string.IsNullOrWhiteSpace(run.Detail) ? null : run.Detail.Trim();

        if (emitted > 0)
        {
            var head = failed > 0
                ? string.Create(Fr, $"Le traitement d'envoi du tenant est terminé : {emitted} document(s) émis, {failed} en échec — consultez les documents en échec.")
                : string.Create(Fr, $"Le traitement d'envoi du tenant est terminé : {emitted} document(s) émis.");
            if (pending > 0)
            {
                head += string.Create(Fr, $" {pending} document(s) restent en attente d'envoi (voir le journal des traitements).");
            }

            return DocumentSendActionResult.Ok(head);
        }

        // Aucun document émis : ce n'est PAS un succès silencieux. On expose le motif (paramétrage manquant,
        // SIREN non publié, contenu différé, rien de prêt…) pour que l'opérateur sache quoi faire (FIX202 : ce
        // motif remonte désormais aussi sur « Envoyer la sélection » et « Tout envoyer », pas seulement le run).
        var none = failed > 0
            ? string.Create(Fr, $"Le traitement d'envoi du tenant est terminé : aucun document émis, {failed} en échec.")
            : "Le traitement d'envoi du tenant est terminé : aucun document émis.";
        return DocumentSendActionResult.Failure(motif is null ? none : none + " " + motif);
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
            var candidates = await FindCompletedManualSendRunsAsync(triggeredAtUtc, cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 1)
            {
                return DescribeSendRunOutcome(candidates[0]);
            }

            if (candidates.Count > 1)
            {
                // Plusieurs envois manuels du tenant se sont clôturés dans la fenêtre (déclenchements concurrents).
                // Le journal ne porte AUCUNE clé de corrélation propre au déclenchement (ni jobId, ni opérateur) :
                // attribuer l'un de ces runs à CE déclenchement risquerait un résultat chiffré FAUX. On renvoie donc
                // au journal (source de vérité) plutôt que d'affirmer un chiffre — jamais un faux résultat (FIX05).
                return JournalFallback;
            }

            if (attempt < _waitPolicy.MaxAttempts)
            {
                await Task.Delay(_waitPolicy.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        // Dégradation gracieuse : le déclenchement a réussi, mais le run n'est pas encore clôturé. On ne prétend
        // PAS qu'il a abouti — on renvoie l'opérateur au journal des traitements (où le résultat ET le motif
        // apparaîtront, colonne Détail visible — FIX05).
        return JournalFallback;
    }

    /// <summary>
    /// Exécutions SEND manuelles CLÔTURÉES dont le début est ≥ <paramref name="triggeredAtUtc"/> (lecture
    /// tenant-scopée, journal rendu le plus récent en tête). En régime nominal (un seul déclenchement) la liste a
    /// 0 (run pas encore clôturé) ou 1 élément ; au-delà, l'appelant détecte l'ambiguïté de corrélation.
    /// </summary>
    private async Task<IReadOnlyList<PipelineRunLogDto>> FindCompletedManualSendRunsAsync(
        DateTimeOffset triggeredAtUtc, CancellationToken cancellationToken)
    {
        var runs = await _runQueries.GetRecentRunsAsync(RunLookupLimit, cancellationToken).ConfigureAwait(false);
        return runs.Where(r =>
            r.RunType == PipelineRunType.Send
            && r.Trigger == PipelineRunTrigger.Manual
            && r.CompletedAt is not null
            && r.StartedAt >= triggeredAtUtc).ToList();
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

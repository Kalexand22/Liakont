namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.Documents.Contracts;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Jobs;
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

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly IDocumentQueries _documents;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IActorContextAccessor _actorAccessor;
    private readonly IActivityLogger _activityLogger;
    private readonly IPermissionService _permissions;

    public DocumentSendActionsService(
        IDocumentQueries documents,
        IServiceScopeFactory scopeFactory,
        IActorContextAccessor actorAccessor,
        IActivityLogger activityLogger,
        IPermissionService permissions)
    {
        _documents = documents;
        _scopeFactory = scopeFactory;
        _actorAccessor = actorAccessor;
        _activityLogger = activityLogger;
        _permissions = permissions;
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

        return DocumentSendActionResult.Ok("Traitement déclenché : le tenant émet ses documents prêts à l'envoi.");
    }

    /// <summary>Identité d'audit de l'opérateur (GUID utilisateur ; « system » si non authentifié) — identique aux endpoints.</summary>
    private static string ActorId(IActorContext actor) =>
        actor.IsAuthenticated ? actor.UserId.ToString() : "system";

    /// <summary>Agrège les motifs d'exclusion en une phrase opérateur (numéro de document inclus — CLAUDE.md n°12).</summary>
    private static string DescribeSkipped(List<string> skipped) =>
        skipped.Count == 0 ? string.Empty : string.Join(" ; ", skipped) + ".";

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

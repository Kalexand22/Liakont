namespace Liakont.Host.Reconciliation;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.Reconciliation.Contracts;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de <see cref="IReconciliationConsoleService"/>. Réutilise les mêmes ports que l'endpoint
/// API04 (<c>ReconciliationEndpointMapping</c>) — <see cref="IReconciliationQueries"/> en lecture et
/// <see cref="IReconciliationService"/> en action — avec la MÊME identité d'opérateur (nom affiché, à défaut
/// e-mail, à défaut identifiant), de sorte que la piste d'audit du canal console et celle du canal HTTP ne
/// divergent pas. Aucune logique métier ni machine à états ici : le rapprochement, l'addendum WORM et l'audit
/// restent dans le module ; on ne fait que vérifier l'autorisation, appeler le port et mapper l'issue en un
/// message opérateur français (CLAUDE.md n°12). Tenant-scopé par construction (la connexion EST le tenant).
/// </summary>
internal sealed class ReconciliationConsoleService : IReconciliationConsoleService
{
    private readonly IReconciliationQueries _queries;
    private readonly IReconciliationService _service;
    private readonly IActorContextAccessor _actorAccessor;
    private readonly IPermissionService _permissions;

    public ReconciliationConsoleService(
        IReconciliationQueries queries,
        IReconciliationService service,
        IActorContextAccessor actorAccessor,
        IPermissionService permissions)
    {
        _queries = queries;
        _service = service;
        _actorAccessor = actorAccessor;
        _permissions = permissions;
    }

    public async Task<ReconciliationQueueViewModel> GetQueueAsync(CancellationToken cancellationToken = default)
    {
        var proposals = await _queries.GetPendingProposalsAsync(cancellationToken).ConfigureAwait(false);
        var orphans = await _queries.GetOrphanPdfsAsync(cancellationToken).ConfigureAwait(false);
        var documentsWithoutPdf = await _queries.GetIssuedDocumentsWithoutPdfAsync(cancellationToken).ConfigureAwait(false);

        return new ReconciliationQueueViewModel
        {
            Proposals = proposals,
            Orphans = orphans,
            DocumentsWithoutPdf = documentsWithoutPdf,
        };
    }

    public async Task<ReconciliationActionResult> ConfirmProposalAsync(Guid queueEntryId, CancellationToken cancellationToken = default)
    {
        if (DenyIfNotAuthorized() is { } denied)
        {
            return denied;
        }

        try
        {
            await _service.ConfirmProposalAsync(queueEntryId, ResolveOperatorIdentity(), cancellationToken).ConfigureAwait(false);
            return ReconciliationActionResult.Ok("Proposition confirmée : le PDF a été rapproché du document.");
        }
        catch (NotFoundException)
        {
            return EntryGoneFailure();
        }
        catch (ConflictException)
        {
            return NotPendingFailure();
        }
    }

    public async Task<ReconciliationActionResult> RejectProposalAsync(Guid queueEntryId, CancellationToken cancellationToken = default)
    {
        if (DenyIfNotAuthorized() is { } denied)
        {
            return denied;
        }

        try
        {
            await _service.RejectProposalAsync(queueEntryId, ResolveOperatorIdentity(), cancellationToken).ConfigureAwait(false);
            return ReconciliationActionResult.Ok("Proposition rejetée : le PDF est reclassé en orphelin, à rattacher manuellement.");
        }
        catch (NotFoundException)
        {
            return EntryGoneFailure();
        }
        catch (ConflictException)
        {
            return NotPendingFailure();
        }
    }

    public async Task<ReconciliationActionResult> LinkManuallyAsync(Guid queueEntryId, Guid documentId, CancellationToken cancellationToken = default)
    {
        if (DenyIfNotAuthorized() is { } denied)
        {
            return denied;
        }

        try
        {
            await _service.ConfirmManualReconciliationAsync(queueEntryId, documentId, ResolveOperatorIdentity(), cancellationToken).ConfigureAwait(false);
            return ReconciliationActionResult.Ok("PDF rattaché au document.");
        }
        catch (NotFoundException)
        {
            return EntryGoneFailure();
        }
        catch (ConflictException)
        {
            return ReconciliationActionResult.Failure(
                "Cette entrée a déjà été rapprochée. Actualisez la liste.");
        }
    }

    private static ReconciliationActionResult EntryGoneFailure() => ReconciliationActionResult.Failure(
        "Entrée introuvable dans ce tenant : elle a peut-être déjà été traitée. Actualisez la liste.");

    private static ReconciliationActionResult NotPendingFailure() => ReconciliationActionResult.Failure(
        "Cette entrée n'est plus une proposition en attente (déjà confirmée ou rejetée). Actualisez la liste.");

    /// <summary>
    /// Refuse l'action si l'opérateur ne porte pas <c>liakont.actions</c> (défense en profondeur : l'endpoint
    /// HTTP porte la même garde via <c>RequireAuthorization</c> ; le chemin in-process de la console ne doit pas
    /// dépendre du seul masquage des boutons côté UI). Renvoie <c>null</c> quand l'action est autorisée.
    /// </summary>
    private ReconciliationActionResult? DenyIfNotAuthorized() =>
        _permissions.HasPermission(LiakontPermissions.Actions)
            ? null
            : ReconciliationActionResult.Failure("Action non autorisée : la permission « actions » (liakont.actions) est requise.");

    /// <summary>
    /// Identité lisible de l'opérateur pour la journalisation, IDENTIQUE à l'endpoint API04
    /// (<c>ResolveOperatorIdentity</c>) : nom affiché, à défaut e-mail, à défaut identifiant. Lève si aucune
    /// identité n'est résolue plutôt que de journaliser une action anonyme (CLAUDE.md n°12) — ne survient pas
    /// après la garde <see cref="DenyIfNotAuthorized"/> (un opérateur autorisé est authentifié).
    /// </summary>
    private string ResolveOperatorIdentity()
    {
        var actor = _actorAccessor.Current;

        if (!string.IsNullOrWhiteSpace(actor.DisplayName))
        {
            return actor.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(actor.Email))
        {
            return actor.Email;
        }

        if (actor.UserId != Guid.Empty)
        {
            return actor.UserId.ToString();
        }

        throw new InvalidOperationException(
            "Identité de l'opérateur introuvable : action de réconciliation impossible sans opérateur (CLAUDE.md n°12).");
    }
}

namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de <see cref="IDocumentResolutionConsoleService"/> pour la console (WEB03c) : appelle le
/// PORT du module Documents (<see cref="IDocumentLifecycle"/>) et journalise l'action (module Audit),
/// EXACTEMENT comme les endpoints API02c <c>resolve-manually</c> / <c>supersede</c> — une seule mécanique de
/// résolution, partagée. Aucune règle métier, aucune machine à états ici (la transition + l'événement d'audit
/// append-only sont écrits par le domaine, dans la même transaction). L'identité d'audit est celle de
/// l'opérateur AUTHENTIFIÉ (jamais une valeur de l'UI — CLAUDE.md n°12). Tenant-scopé par construction
/// (la connexion EST le tenant — CLAUDE.md n°9).
/// </summary>
internal sealed class DocumentResolutionConsoleService : IDocumentResolutionConsoleService
{
    private const string DocumentEntityType = "Document";

    /// <summary>Borne du sélecteur de remplacement : premiers candidats affichés (la recherche affine).</summary>
    private const int CandidatePageSize = 20;

    private readonly IDocumentLifecycle _lifecycle;
    private readonly IDocumentQueries _documents;
    private readonly IActorContextAccessor _actorContext;
    private readonly IActivityLogger _activityLogger;

    public DocumentResolutionConsoleService(
        IDocumentLifecycle lifecycle,
        IDocumentQueries documents,
        IActorContextAccessor actorContext,
        IActivityLogger activityLogger)
    {
        _lifecycle = lifecycle;
        _documents = documents;
        _actorContext = actorContext;
        _activityLogger = activityLogger;
    }

    public async Task<DocumentResolutionConsoleStatus> ResolveManuallyAsync(
        Guid documentId, string? reason, CancellationToken cancellationToken = default)
    {
        // Motif OBLIGATOIRE, vérifié AVANT le port (le domaine lèverait sinon — parité avec le 400 de l'endpoint).
        if (string.IsNullOrWhiteSpace(reason))
        {
            return DocumentResolutionConsoleStatus.ReasonRequired;
        }

        var actor = _actorContext.Current;
        var operatorId = ActorId(actor);

        var outcome = await _lifecycle.ResolveManuallyAsync(documentId, reason, operatorId, cancellationToken).ConfigureAwait(false);
        if (outcome is not DocumentResolutionOutcome.Succeeded)
        {
            return Map(outcome);
        }

        // Audit (module Audit) en complément du DocumentEvent écrit par le port : awaité avant de rendre la
        // main (anti faux-vert), même type/message que l'endpoint API02c pour une empreinte d'audit identique.
        await _activityLogger.LogActivityAsync(
            DocumentEntityType,
            documentId.ToString(),
            "documents.resolved_manually",
            string.Create(CultureInfo.InvariantCulture, $"Document {documentId} marqué « traité manuellement hors passerelle » par l'opérateur. Motif : {reason}"),
            operatorId,
            metadata: new { reason },
            companyId: actor.CompanyId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return DocumentResolutionConsoleStatus.Succeeded;
    }

    public async Task<DocumentResolutionConsoleStatus> SupersedeAsync(
        Guid documentId, Guid replacementDocumentId, CancellationToken cancellationToken = default)
    {
        if (replacementDocumentId == Guid.Empty)
        {
            return DocumentResolutionConsoleStatus.ReplacementRequired;
        }

        if (replacementDocumentId == documentId)
        {
            return DocumentResolutionConsoleStatus.ReplacementIsSelf;
        }

        var actor = _actorContext.Current;
        var operatorId = ActorId(actor);

        var outcome = await _lifecycle.SupersedeAsync(documentId, replacementDocumentId, operatorId, cancellationToken).ConfigureAwait(false);
        if (outcome is not DocumentResolutionOutcome.Succeeded)
        {
            return Map(outcome);
        }

        await _activityLogger.LogActivityAsync(
            DocumentEntityType,
            documentId.ToString(),
            "documents.superseded",
            string.Create(CultureInfo.InvariantCulture, $"Document {documentId} (rejeté) lié à son remplaçant {replacementDocumentId} — passé à l'état Superseded par l'opérateur."),
            operatorId,
            metadata: new { replacementDocumentId },
            companyId: actor.CompanyId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return DocumentResolutionConsoleStatus.Succeeded;
    }

    public async Task<IReadOnlyList<DocumentReplacementCandidate>> SearchReplacementCandidatesAsync(
        Guid rejectedDocumentId, string? search, CancellationToken cancellationToken = default)
    {
        // On demande une page de plus que la borne d'affichage : exclure le document rejeté lui-même de la
        // page ne doit pas faire descendre la liste sous CandidatePageSize candidats valides.
        var filter = new DocumentListFilter
        {
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            Page = 1,
            PageSize = CandidatePageSize + 1,
        };

        var result = await _documents.GetDocumentsAsync(filter, cancellationToken).ConfigureAwait(false);

        var candidates = new List<DocumentReplacementCandidate>(result.Items.Count);
        foreach (var item in result.Items)
        {
            // Le remplaçant doit être un AUTRE document (un document ne se remplace pas lui-même).
            if (item.Id == rejectedDocumentId)
            {
                continue;
            }

            candidates.Add(new DocumentReplacementCandidate
            {
                Id = item.Id,
                DocumentNumber = item.DocumentNumber,
                CustomerName = item.CustomerName,
                IssueDate = item.IssueDate,
                TotalGross = item.TotalGross,
                State = item.State,
            });

            if (candidates.Count >= CandidatePageSize)
            {
                break;
            }
        }

        return candidates;
    }

    /// <summary>Identité d'audit de l'opérateur (GUID utilisateur ; « system » si non authentifié) — parité avec l'endpoint API02c.</summary>
    private static string ActorId(IActorContext actor) =>
        actor.IsAuthenticated ? actor.UserId.ToString() : "system";

    /// <summary>Reporte le résultat du port (refus attendu) sur le statut console — <c>Succeeded</c> est traité par l'appelant avant l'audit.</summary>
    private static DocumentResolutionConsoleStatus Map(DocumentResolutionOutcome outcome) => outcome switch
    {
        DocumentResolutionOutcome.DocumentNotFound => DocumentResolutionConsoleStatus.DocumentNotFound,
        DocumentResolutionOutcome.InvalidState => DocumentResolutionConsoleStatus.InvalidState,
        DocumentResolutionOutcome.ReplacementNotFound => DocumentResolutionConsoleStatus.ReplacementNotFound,
        _ => DocumentResolutionConsoleStatus.InvalidState,
    };
}

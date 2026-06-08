namespace Liakont.Modules.Documents.Infrastructure.Lifecycle;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Application;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Implémentation du port <see cref="IDocumentLifecycle"/> (consommé par le pipeline, PIP01c) : applique
/// une transition de la machine à états (TRK02) via l'unité de travail transactionnelle du module — verrou
/// <c>FOR UPDATE</c>, transition de domaine, puis persistance ATOMIQUE de l'état ET de son événement
/// d'audit append-only (CLAUDE.md n°4). Tenant-scopé par la connexion (database-per-tenant). L'horloge est
/// injectable (tests) avec un défaut sûr <see cref="TimeProvider.System"/> (même motif que
/// <c>DocumentReconciliationJournal</c>).
/// </summary>
internal sealed class DocumentLifecycle : IDocumentLifecycle
{
    private readonly IDocumentUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;

    public DocumentLifecycle(IDocumentUnitOfWorkFactory unitOfWorkFactory)
        : this(unitOfWorkFactory, TimeProvider.System)
    {
    }

    internal DocumentLifecycle(IDocumentUnitOfWorkFactory unitOfWorkFactory, TimeProvider timeProvider)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _timeProvider = timeProvider;
    }

    public Task BlockAsync(Guid documentId, string reason, CancellationToken cancellationToken = default) =>
        TransitionAsync(documentId, (document, at) => document.MarkBlocked(at, reason), cancellationToken);

    public Task MarkReadyToSendAsync(Guid documentId, string mappingVersion, CancellationToken cancellationToken = default) =>
        TransitionAsync(documentId, (document, at) => document.MarkReadyToSendWithMapping(at, mappingVersion), cancellationToken);

    public Task BeginSendingAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        TransitionAsync(documentId, (document, at) => document.BeginSending(at), cancellationToken);

    public Task MarkIssuedAsync(Guid documentId, DocumentIssuanceSnapshots snapshots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var domainSnapshots = new IssuanceSnapshots(
            snapshots.PayloadSnapshot, snapshots.PaResponseSnapshot, snapshots.MappingTrace, snapshots.PaDocumentId);
        return TransitionAsync(documentId, (document, at) => document.MarkIssued(domainSnapshots, at), cancellationToken);
    }

    public Task MarkRejectedByPaAsync(Guid documentId, DocumentRejectionSnapshots snapshots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var domainSnapshots = new RejectionSnapshots(snapshots.PayloadSnapshot, snapshots.PaResponseSnapshot);
        return TransitionAsync(documentId, (document, at) => document.MarkRejectedByPa(domainSnapshots, at), cancellationToken);
    }

    public Task MarkTechnicalErrorAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        TransitionAsync(documentId, (document, at) => document.MarkTechnicalError(at), cancellationToken);

    public async Task<DocumentResolutionOutcome> ResolveManuallyAsync(
        Guid documentId, string reason, string operatorIdentity, CancellationToken cancellationToken = default)
    {
        await using IDocumentUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        Document? document = await unitOfWork.GetForUpdateAsync(documentId, cancellationToken);
        if (document is null)
        {
            return DocumentResolutionOutcome.DocumentNotFound;
        }

        // Sources autorisées de « traité manuellement » : Blocked ou RejectedByPa (machine à états TRK02).
        // Vérifié SOUS le verrou FOR UPDATE (autoritaire — pas de TOCTOU) plutôt que de laisser la transition lever.
        if (document.State is not (DocumentState.Blocked or DocumentState.RejectedByPa))
        {
            return DocumentResolutionOutcome.InvalidState;
        }

        DocumentEvent documentEvent = document.MarkManuallyHandled(reason, operatorIdentity, _timeProvider.GetUtcNow());
        await unitOfWork.UpsertDocumentAsync(document, cancellationToken);
        await unitOfWork.AppendEventAsync(documentEvent, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return DocumentResolutionOutcome.Succeeded;
    }

    public async Task<DocumentResolutionOutcome> SupersedeAsync(
        Guid documentId, Guid replacementDocumentId, string operatorIdentity, CancellationToken cancellationToken = default)
    {
        await using IDocumentUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        Document? document = await unitOfWork.GetForUpdateAsync(documentId, cancellationToken);
        if (document is null)
        {
            return DocumentResolutionOutcome.DocumentNotFound;
        }

        // L'état est vérifié AVANT de charger le remplaçant : seul un document RejectedByPa peut être remplacé.
        if (document.State is not DocumentState.RejectedByPa)
        {
            return DocumentResolutionOutcome.InvalidState;
        }

        // Le remplaçant doit EXISTER dans le tenant courant (même connexion = même tenant — database-per-tenant).
        // On le charge pour obtenir sa référence AUTORITAIRE (numéro créé par le logiciel source, F06 §4) : la
        // passerelle ne fabrique jamais de numéro de remplacement.
        Document? replacement = await unitOfWork.GetForUpdateAsync(replacementDocumentId, cancellationToken);
        if (replacement is null)
        {
            return DocumentResolutionOutcome.ReplacementNotFound;
        }

        DocumentEvent documentEvent = document.Supersede(replacement.DocumentNumber, operatorIdentity, _timeProvider.GetUtcNow());
        await unitOfWork.UpsertDocumentAsync(document, cancellationToken);
        await unitOfWork.AppendEventAsync(documentEvent, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return DocumentResolutionOutcome.Succeeded;
    }

    private async Task TransitionAsync(
        Guid documentId,
        Func<Document, DateTimeOffset, DocumentEvent> transition,
        CancellationToken cancellationToken)
    {
        await using IDocumentUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        // Read-modify-write sous verrou (FOR UPDATE) : la transition est sérialisée par document.
        Document? document = await unitOfWork.GetForUpdateAsync(documentId, cancellationToken);
        if (document is null)
        {
            throw new InvalidOperationException(
                $"Document {documentId} introuvable : impossible d'appliquer une transition de cycle de vie (pipeline PIP01).");
        }

        DocumentEvent documentEvent = transition(document, _timeProvider.GetUtcNow());
        await unitOfWork.UpsertDocumentAsync(document, cancellationToken);
        await unitOfWork.AppendEventAsync(documentEvent, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

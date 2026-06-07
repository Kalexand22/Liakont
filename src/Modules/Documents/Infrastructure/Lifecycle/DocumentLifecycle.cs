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

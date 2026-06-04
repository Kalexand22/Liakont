namespace Liakont.Modules.Documents.Infrastructure.Reconciliation;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Application;
using Liakont.Modules.Documents.Contracts.Reconciliation;
using Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Implémentation du port <see cref="IDocumentReconciliationJournal"/> (item TRK07) : inscrit sur un
/// document émis un <see cref="DocumentEvent"/> de rapprochement, via l'unité de travail transactionnelle
/// du module Documents. L'événement est ajouté dans la même transaction que la vérification d'existence
/// du document (verrou <c>FOR UPDATE</c>), append-only par construction (CLAUDE.md n°4). Tenant-scopé par
/// la connexion (database-per-tenant). L'horloge est injectable (tests) avec un défaut sûr
/// <see cref="TimeProvider.System"/> (même motif que <c>CollaborationService</c>).
/// </summary>
internal sealed class DocumentReconciliationJournal : IDocumentReconciliationJournal
{
    private readonly IDocumentUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;

    public DocumentReconciliationJournal(IDocumentUnitOfWorkFactory unitOfWorkFactory)
        : this(unitOfWorkFactory, TimeProvider.System)
    {
    }

    internal DocumentReconciliationJournal(IDocumentUnitOfWorkFactory unitOfWorkFactory, TimeProvider timeProvider)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _timeProvider = timeProvider;
    }

    public Task RecordAutomaticReconciliationAsync(Guid documentId, string detail, CancellationToken cancellationToken = default) =>
        AppendAsync(documentId, DocumentEvent.ReconciledAutomatically(documentId, _timeProvider.GetUtcNow(), detail), cancellationToken);

    public Task RecordManualReconciliationAsync(Guid documentId, string detail, string operatorIdentity, CancellationToken cancellationToken = default) =>
        AppendAsync(documentId, DocumentEvent.ReconciledManually(documentId, _timeProvider.GetUtcNow(), detail, operatorIdentity), cancellationToken);

    private async Task AppendAsync(Guid documentId, DocumentEvent documentEvent, CancellationToken cancellationToken)
    {
        await using IDocumentUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        // Vérifie l'existence du document émis (verrou FOR UPDATE) AVANT d'inscrire le fait d'audit : un
        // rapprochement ne peut porter que sur un document connu (la clé étrangère le garantirait aussi,
        // mais le message opérateur français est plus clair, CLAUDE.md n°12).
        Document? document = await unitOfWork.GetForUpdateAsync(documentId, cancellationToken);
        if (document is null)
        {
            throw new InvalidOperationException(
                $"Document {documentId} introuvable : impossible de journaliser un rapprochement PDF (F06 §7).");
        }

        await unitOfWork.AppendEventAsync(documentEvent, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

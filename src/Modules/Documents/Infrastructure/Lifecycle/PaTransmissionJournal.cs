namespace Liakont.Modules.Documents.Infrastructure.Lifecycle;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Application;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Implémentation du port <see cref="IPaTransmissionJournal"/> (consommé par le pipeline, FX07) : ajoute, en
/// APPEND-ONLY, un fait d'audit de journalisation d'envoi PA sur <c>documents.document_events</c> (FX06),
/// via l'unité de travail transactionnelle du module. C'est un PUR ajout d'événement — AUCUNE transition de
/// la machine à états ni upsert de document (à la différence de <see cref="DocumentLifecycle"/>) : la
/// transmission n'est pas un changement d'état du document, juste une trace d'audit. Tenant-scopé par la
/// connexion (database-per-tenant) ; horloge injectable (tests) avec défaut sûr
/// <see cref="TimeProvider.System"/> (même motif que <see cref="DocumentLifecycle"/>).
/// </summary>
internal sealed class PaTransmissionJournal : IPaTransmissionJournal
{
    private readonly IDocumentUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;

    public PaTransmissionJournal(IDocumentUnitOfWorkFactory unitOfWorkFactory)
        : this(unitOfWorkFactory, TimeProvider.System)
    {
    }

    internal PaTransmissionJournal(IDocumentUnitOfWorkFactory unitOfWorkFactory, TimeProvider timeProvider)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _timeProvider = timeProvider;
    }

    public async Task JournalAsync(PaTransmissionJournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // L'horodatage du fait d'audit est posé par l'horloge du module (cohérent avec les autres événements),
        // distinct des horodatages métier requête/réponse PA portés par l'entrée.
        DocumentEvent journal = DocumentEvent.PaTransmissionJournaled(
            entry.DocumentId,
            occurredAtUtc: _timeProvider.GetUtcNow(),
            paAccount: entry.PaAccount,
            paPluginId: entry.PaPluginId,
            paRequestUtc: entry.PaRequestUtc,
            paResponseUtc: entry.PaResponseUtc,
            transmittedArtifactHash: entry.TransmittedArtifactHash,
            idempotencyKey: entry.IdempotencyKey,
            paResponseSnapshot: entry.PaResponseSnapshot,
            detail: entry.Detail);

        await using IDocumentUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);
        await unitOfWork.AppendEventAsync(journal, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

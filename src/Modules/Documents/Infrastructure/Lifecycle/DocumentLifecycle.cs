namespace Liakont.Modules.Documents.Infrastructure.Lifecycle;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Application;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Domain.StateMachine;

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
    private readonly IDocumentQueries _documentQueries;
    private readonly TimeProvider _timeProvider;

    public DocumentLifecycle(IDocumentUnitOfWorkFactory unitOfWorkFactory, IDocumentQueries documentQueries)
        : this(unitOfWorkFactory, documentQueries, TimeProvider.System)
    {
    }

    internal DocumentLifecycle(IDocumentUnitOfWorkFactory unitOfWorkFactory, IDocumentQueries documentQueries, TimeProvider timeProvider)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _documentQueries = documentQueries;
        _timeProvider = timeProvider;
    }

    public Task BlockAsync(Guid documentId, string reason, CancellationToken cancellationToken = default) =>
        TransitionAsync(documentId, (document, at) => document.MarkBlocked(at, reason), cancellationToken);

    public Task MarkReadyToSendAsync(Guid documentId, string mappingVersion, CancellationToken cancellationToken = default) =>
        TransitionAsync(documentId, (document, at) => document.MarkReadyToSendWithMapping(at, mappingVersion), cancellationToken);

    public async Task<DocumentRecheckPersistOutcome> MarkReadyToSendByRecheckAsync(
        Guid documentId, string mappingVersion, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
    {
        await using IDocumentUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        Document? document = await unitOfWork.GetForUpdateAsync(documentId, cancellationToken);
        if (document is null)
        {
            return DocumentRecheckPersistOutcome.DocumentNotFound;
        }

        // Légalité du déblocage vérifiée SOUS le verrou (source unique : la machine à états), comme
        // ResolveManuallyAsync : un geste opérateur concurrent qui a sorti le document de Blocked est un refus
        // ATTENDU retourné comme résultat — jamais une exception (pas de 500). Une vraie erreur de persistance
        // (CommitAsync) reste, elle, une exception qui remonte.
        if (!DocumentStateMachine.IsAllowed(document.State, DocumentState.ReadyToSend))
        {
            return DocumentRecheckPersistOutcome.StateChanged;
        }

        DocumentEvent documentEvent = document.MarkReadyToSendWithMapping(
            _timeProvider.GetUtcNow(), mappingVersion, detail: "Débloqué par re-vérification de l'opérateur.", operatorIdentity: operatorIdentity, operatorName: operatorName);
        await unitOfWork.UpsertDocumentAsync(document, cancellationToken);
        await unitOfWork.AppendEventAsync(documentEvent, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return DocumentRecheckPersistOutcome.Persisted;
    }

    public async Task<DocumentRecheckPersistOutcome> RecordRecheckStillBlockedAsync(
        Guid documentId, string reevaluatedReason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
    {
        await using IDocumentUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        Document? document = await unitOfWork.GetForUpdateAsync(documentId, cancellationToken);
        if (document is null)
        {
            return DocumentRecheckPersistOutcome.DocumentNotFound;
        }

        // On n'inscrit le fait d'audit « toujours bloqué » QUE si le document est encore Blocked SOUS le verrou :
        // un geste concurrent qui l'a débloqué/résolu est un refus attendu (StateChanged), pas un faux audit ni un 500.
        if (document.State != DocumentState.Blocked)
        {
            return DocumentRecheckPersistOutcome.StateChanged;
        }

        DocumentEvent documentEvent = document.RecordRecheckStillBlocked(reevaluatedReason, operatorIdentity, _timeProvider.GetUtcNow(), operatorName);
        await unitOfWork.UpsertDocumentAsync(document, cancellationToken);
        await unitOfWork.AppendEventAsync(documentEvent, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return DocumentRecheckPersistOutcome.Persisted;
    }

    public Task BeginSendingAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        TransitionAsync(documentId, (document, at) => document.BeginSending(at), cancellationToken);

    public Task RecordPaSendingReferenceAsync(Guid documentId, string paDocumentId, string? paResponseSnapshot, CancellationToken cancellationToken = default) =>
        TransitionAsync(documentId, (document, at) => document.RecordPaSendingReference(paDocumentId, paResponseSnapshot, at), cancellationToken);

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
        Guid documentId, string reason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
    {
        await using IDocumentUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        Document? document = await unitOfWork.GetForUpdateAsync(documentId, cancellationToken);
        if (document is null)
        {
            return DocumentResolutionOutcome.DocumentNotFound;
        }

        // Source autoritaire des états-sources autorisés : la machine à états du domaine (TRK02). Vérifié SOUS le
        // verrou FOR UPDATE (pas de TOCTOU) plutôt que de laisser la transition lever, et SANS dupliquer la liste
        // (toute évolution future de la machine à états reste honorée sans dérive silencieuse).
        if (!DocumentStateMachine.IsAllowed(document.State, DocumentState.ManuallyHandled))
        {
            return DocumentResolutionOutcome.InvalidState;
        }

        DocumentEvent documentEvent = document.MarkManuallyHandled(reason, operatorIdentity, _timeProvider.GetUtcNow(), operatorName);
        await unitOfWork.UpsertDocumentAsync(document, cancellationToken);
        await unitOfWork.AppendEventAsync(documentEvent, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return DocumentResolutionOutcome.Succeeded;
    }

    public async Task<DocumentResolutionOutcome> SupersedeAsync(
        Guid documentId, Guid replacementDocumentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default)
    {
        await using IDocumentUnitOfWork unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        Document? document = await unitOfWork.GetForUpdateAsync(documentId, cancellationToken);
        if (document is null)
        {
            return DocumentResolutionOutcome.DocumentNotFound;
        }

        // L'état est vérifié (via la machine à états, source unique de vérité) AVANT de charger le remplaçant.
        if (!DocumentStateMachine.IsAllowed(document.State, DocumentState.Superseded))
        {
            return DocumentResolutionOutcome.InvalidState;
        }

        // Le remplaçant doit EXISTER dans le tenant courant. Lu SANS verrou (read port tenant-scopé) : son
        // DocumentNumber est immuable (fixé en CreateDetected, jamais modifié par une transition) et un document
        // n'est jamais supprimé ⇒ pas de TOCTOU. On évite ainsi un verrou FOR UPDATE inutile sur une ligne non
        // mutée (et la fenêtre de deadlock par ordre de verrouillage entre deux supersede croisés).
        DocumentDto? replacement = await _documentQueries.GetByIdAsync(replacementDocumentId, cancellationToken);
        if (replacement is null)
        {
            return DocumentResolutionOutcome.ReplacementNotFound;
        }

        DocumentEvent documentEvent = document.Supersede(replacement.DocumentNumber, operatorIdentity, _timeProvider.GetUtcNow(), operatorName);
        await unitOfWork.UpsertDocumentAsync(document, cancellationToken);
        await unitOfWork.AppendEventAsync(documentEvent, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return DocumentResolutionOutcome.Succeeded;
    }

    public Task ConfirmBuyerAsIndividualAsync(Guid documentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) =>
        TransitionAsync(documentId, (document, at) => document.ConfirmBuyerAsIndividual(operatorIdentity, at, operatorName), cancellationToken);

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

namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Documents.Application;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Infrastructure.Lifecycle;
using Xunit;

/// <summary>
/// <see cref="DocumentLifecycle"/> (PIP01a) : orchestration read-modify-write via l'unité de travail —
/// charge sous verrou, applique la transition de domaine, persiste état + audit, puis commit. Lève si le
/// document est inconnu (sans commit). Le détail de la machine à états est testé côté Domain/Integration.
/// </summary>
public sealed class DocumentLifecycleTests
{
    private static readonly DateTimeOffset At = new(2026, 5, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BlockAsync_Transitions_And_Persists_State_And_Audit_Then_Commits()
    {
        var document = Detected();
        var unitOfWork = new FakeUnitOfWork(document);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork), new FakeQueries());

        await lifecycle.BlockAsync(document.Id, "Table TVA non validée");

        document.State.Should().Be(DocumentState.Blocked);
        unitOfWork.Upserted.Should().BeSameAs(document);
        unitOfWork.AppendedEvents.Should().ContainSingle();
        unitOfWork.AppendedEvents[0].EventType.Should().Be(DocumentEventType.DocumentBlocked);
        unitOfWork.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkReadyToSendAsync_Records_The_Mapping_Version()
    {
        var document = Detected();
        var unitOfWork = new FakeUnitOfWork(document);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork), new FakeQueries());

        await lifecycle.MarkReadyToSendAsync(document.Id, "2026.1");

        document.State.Should().Be(DocumentState.ReadyToSend);
        document.MappingVersion.Should().Be("2026.1");
        unitOfWork.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_Document_Throws_And_Does_Not_Commit()
    {
        var unitOfWork = new FakeUnitOfWork(document: null);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork), new FakeQueries());

        var act = async () => await lifecycle.BlockAsync(Guid.NewGuid(), "motif");

        await act.Should().ThrowAsync<InvalidOperationException>();
        unitOfWork.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task RecordRecheckStillBlockedAsync_On_A_Blocked_Document_Appends_Operator_Event_And_Commits()
    {
        var document = Blocked();
        var unitOfWork = new FakeUnitOfWork(document);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork), new FakeQueries());

        var outcome = await lifecycle.RecordRecheckStillBlockedAsync(document.Id, "Acheteur professionnel non confirmé.", "alice@cmp", "Alice Comptable");

        outcome.Should().Be(DocumentRecheckPersistOutcome.Persisted);
        document.State.Should().Be(DocumentState.Blocked, "le recheck toujours bloqué ne change pas l'état");
        unitOfWork.AppendedEvents.Should().ContainSingle().Which.EventType.Should().Be(DocumentEventType.DocumentRecheckedStillBlocked);
        unitOfWork.AppendedEvents[0].OperatorIdentity.Should().Be("alice@cmp");
        unitOfWork.AppendedEvents[0].OperatorName.Should().Be("Alice Comptable", "le nom d'affichage de l'opérateur est persisté AVEC l'événement (FIX305)");
        unitOfWork.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task RecordRecheckStillBlockedAsync_When_No_Longer_Blocked_Returns_StateChanged_Without_Writing()
    {
        // FIX02 : un geste concurrent a sorti le document de Blocked sous le verrou → refus gracieux, AUCUN faux
        // audit, AUCUN commit (et surtout pas une exception → pas de 500).
        var document = Detected();
        var unitOfWork = new FakeUnitOfWork(document);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork), new FakeQueries());

        var outcome = await lifecycle.RecordRecheckStillBlockedAsync(document.Id, "Motif.", "alice@cmp", "Alice Comptable");

        outcome.Should().Be(DocumentRecheckPersistOutcome.StateChanged);
        unitOfWork.AppendedEvents.Should().BeEmpty("aucun fait d'audit n'est inscrit sur un document qui n'est plus bloqué");
        unitOfWork.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task MarkReadyToSendByRecheckAsync_On_A_Blocked_Document_Unblocks_With_Operator_And_Commits()
    {
        var document = Blocked();
        var unitOfWork = new FakeUnitOfWork(document);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork), new FakeQueries());

        var outcome = await lifecycle.MarkReadyToSendByRecheckAsync(document.Id, "2026.1", "alice@cmp", "Alice Comptable");

        outcome.Should().Be(DocumentRecheckPersistOutcome.Persisted);
        document.State.Should().Be(DocumentState.ReadyToSend);
        document.MappingVersion.Should().Be("2026.1");
        unitOfWork.AppendedEvents.Should().ContainSingle().Which.EventType.Should().Be(DocumentEventType.DocumentReadyToSend);
        unitOfWork.AppendedEvents[0].OperatorIdentity.Should().Be("alice@cmp", "le déblocage par recheck est attribué à l'opérateur");
        unitOfWork.AppendedEvents[0].OperatorName.Should().Be("Alice Comptable", "le nom d'affichage de l'opérateur est persisté AVEC l'événement de déblocage (FIX305)");
        unitOfWork.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkReadyToSendByRecheckAsync_When_Already_Unblocked_Returns_StateChanged_Without_Writing()
    {
        // Course concurrente : un autre recheck a déjà débloqué le document (ReadyToSend) → ReadyToSend → ReadyToSend
        // est illégal ; refus gracieux sans rien écrire, jamais une exception.
        var document = Blocked();
        document.MarkReadyToSend(At.AddMinutes(5));
        var unitOfWork = new FakeUnitOfWork(document);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork), new FakeQueries());

        var outcome = await lifecycle.MarkReadyToSendByRecheckAsync(document.Id, "2026.1", "alice@cmp", "Alice Comptable");

        outcome.Should().Be(DocumentRecheckPersistOutcome.StateChanged);
        unitOfWork.AppendedEvents.Should().BeEmpty();
        unitOfWork.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task Recheck_Persistence_On_An_Unknown_Document_Returns_DocumentNotFound_Without_Throwing()
    {
        var unitOfWork = new FakeUnitOfWork(document: null);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork), new FakeQueries());

        (await lifecycle.RecordRecheckStillBlockedAsync(Guid.NewGuid(), "Motif.", "alice@cmp", "Alice Comptable"))
            .Should().Be(DocumentRecheckPersistOutcome.DocumentNotFound);
        (await lifecycle.MarkReadyToSendByRecheckAsync(Guid.NewGuid(), "2026.1", "alice@cmp", "Alice Comptable"))
            .Should().Be(DocumentRecheckPersistOutcome.DocumentNotFound);
        (await lifecycle.MarkBlockedByRecheckAsync(Guid.NewGuid(), "Motif.", "alice@cmp", "Alice Comptable"))
            .Should().Be(DocumentRecheckPersistOutcome.DocumentNotFound);
        unitOfWork.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task MarkBlockedByRecheckAsync_On_A_Rejected_Document_Transitions_To_Blocked_With_Operator_And_Commits()
    {
        // Re-vérification d'un document rejeté par la PA dont la cause n'est pas corrigée : RejectedByPa → Blocked,
        // fait d'audit attribué à l'opérateur portant le motif réévalué (devient le motif courant).
        var document = RejectedByPa();
        var unitOfWork = new FakeUnitOfWork(document);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork), new FakeQueries());

        var outcome = await lifecycle.MarkBlockedByRecheckAsync(document.Id, "Mentions B2B manquantes.", "alice@cmp", "Alice Comptable");

        outcome.Should().Be(DocumentRecheckPersistOutcome.Persisted);
        document.State.Should().Be(DocumentState.Blocked, "un rejeté re-vérifié non prêt quitte le cul-de-sac pour Blocked");
        unitOfWork.AppendedEvents.Should().ContainSingle().Which.EventType.Should().Be(DocumentEventType.DocumentBlocked);
        unitOfWork.AppendedEvents[0].OperatorIdentity.Should().Be("alice@cmp", "la transition de blocage par recheck est attribuée à l'opérateur");
        unitOfWork.AppendedEvents[0].OperatorName.Should().Be("Alice Comptable", "le nom d'affichage de l'opérateur est persisté AVEC l'événement (FIX305)");
        unitOfWork.AppendedEvents[0].Detail.Should().Contain("Mentions B2B manquantes.", "le motif réévalué est porté dans la piste d'audit");
        unitOfWork.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkBlockedByRecheckAsync_When_No_Longer_Rejected_Returns_StateChanged_Without_Writing()
    {
        // Course concurrente : un geste opérateur a déjà sorti le document de RejectedByPa (RejectedByPa → Blocked
        // est alors illégal depuis Blocked) → refus gracieux, AUCUN faux audit, AUCUN commit, jamais une exception.
        var document = Blocked();
        var unitOfWork = new FakeUnitOfWork(document);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork), new FakeQueries());

        var outcome = await lifecycle.MarkBlockedByRecheckAsync(document.Id, "Motif.", "alice@cmp", "Alice Comptable");

        outcome.Should().Be(DocumentRecheckPersistOutcome.StateChanged);
        unitOfWork.AppendedEvents.Should().BeEmpty("aucun fait d'audit n'est inscrit sur un document qui n'est plus rejeté");
        unitOfWork.Committed.Should().BeFalse();
    }

    private static Document Detected() => Document.CreateDetected(
        Guid.NewGuid(),
        "SRC-1",
        "F-2026-001",
        "FAC",
        new DateOnly(2026, 5, 14),
        supplierSiren: "123456789",
        customerName: "Client SARL",
        customerIsCompanyHint: true,
        totalNet: 100.00m,
        totalTax: 20.00m,
        totalGross: 120.00m,
        payloadHash: "hash-1",
        detectedAtUtc: At);

    private static Document Blocked()
    {
        var document = Detected();
        document.MarkBlocked(At.AddMinutes(1), "Table TVA non validée");
        return document;
    }

    private static Document RejectedByPa()
    {
        var document = Detected();
        document.MarkReadyToSendWithMapping(At.AddMinutes(1), "2026.1");
        document.BeginSending(At.AddMinutes(2));
        document.MarkRejectedByPa(new RejectionSnapshots("{\"payload\":true}", "{\"rejected\":true}"), At.AddMinutes(3), "Rejeté : mentions B2B manquantes.");
        return document;
    }

    private sealed class FakeFactory : IDocumentUnitOfWorkFactory
    {
        private readonly IDocumentUnitOfWork _unitOfWork;

        public FakeFactory(IDocumentUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public Task<IDocumentUnitOfWork> BeginAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_unitOfWork);
    }

    private sealed class FakeQueries : IDocumentQueries
    {
        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IDocumentUnitOfWork
    {
        private readonly Document? _document;

        public FakeUnitOfWork(Document? document) => _document = document;

        public Document? Upserted { get; private set; }

        public List<DocumentEvent> AppendedEvents { get; } = new();

        public bool Committed { get; private set; }

        public Task<bool> CreateDetectedAsync(Document document, DocumentEvent genesisEvent, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<Document?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_document);

        public Task UpsertDocumentAsync(Document document, CancellationToken cancellationToken = default)
        {
            Upserted = document;
            return Task.CompletedTask;
        }

        public Task AppendEventAsync(DocumentEvent documentEvent, CancellationToken cancellationToken = default)
        {
            AppendedEvents.Add(documentEvent);
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

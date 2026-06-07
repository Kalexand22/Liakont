namespace Liakont.Modules.Documents.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Documents.Application;
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
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork));

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
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork));

        await lifecycle.MarkReadyToSendAsync(document.Id, "2026.1");

        document.State.Should().Be(DocumentState.ReadyToSend);
        document.MappingVersion.Should().Be("2026.1");
        unitOfWork.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_Document_Throws_And_Does_Not_Commit()
    {
        var unitOfWork = new FakeUnitOfWork(document: null);
        var lifecycle = new DocumentLifecycle(new FakeFactory(unitOfWork));

        var act = async () => await lifecycle.BlockAsync(Guid.NewGuid(), "motif");

        await act.Should().ThrowAsync<InvalidOperationException>();
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

    private sealed class FakeFactory : IDocumentUnitOfWorkFactory
    {
        private readonly IDocumentUnitOfWork _unitOfWork;

        public FakeFactory(IDocumentUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public Task<IDocumentUnitOfWork> BeginAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_unitOfWork);
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

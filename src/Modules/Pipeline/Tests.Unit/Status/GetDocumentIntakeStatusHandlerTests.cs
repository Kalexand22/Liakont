namespace Liakont.Modules.Pipeline.Tests.Unit.Status;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Infrastructure.Status;
using Xunit;

/// <summary>
/// Sémantique terminale du point de statut agent (PIP01d, ADR-0012) : absence de Document = Pending (jamais 404,
/// jamais terminal) ; présence (Detected et au-delà, Issued INCLUS) = Processed. La clé et l'empreinte interrogées
/// sont fidèlement renvoyées.
/// </summary>
public sealed class GetDocumentIntakeStatusHandlerTests
{
    private const string SourceReference = "BVL-2026-42";
    private const string PayloadHash = "abc123def456";

    [Fact]
    public async Task Unknown_Key_Maps_To_Pending()
    {
        var handler = new GetDocumentIntakeStatusHandler(new StubDocumentQueries(status: null));

        var result = await handler.Handle(Query(), CancellationToken.None);

        result.Status.Should().Be(DocumentIntakeStatus.Pending, "une clé inconnue est reçue mais pas encore rangée — l'agent renvoie.");
        result.SourceReference.Should().Be(SourceReference);
        result.PayloadHash.Should().Be(PayloadHash);
        result.Reason.Should().BeNull();
    }

    [Theory]
    [InlineData("Detected")]
    [InlineData("Blocked")]
    [InlineData("ReadyToSend")]
    [InlineData("Issued")]
    public async Task Existing_Document_Maps_To_Processed(string state)
    {
        var status = new DocumentStatusDto { Id = Guid.NewGuid(), DocumentNumber = "F-1", State = state };
        var handler = new GetDocumentIntakeStatusHandler(new StubDocumentQueries(status));

        var result = await handler.Handle(Query(), CancellationToken.None);

        result.Status.Should().Be(
            DocumentIntakeStatus.Processed,
            "un Document existe pour la clé ({0}) — la plateforme a pris la responsabilité du document (Issued inclus).",
            state);
    }

    private static GetDocumentIntakeStatusQuery Query() =>
        new() { SourceReference = SourceReference, PayloadHash = PayloadHash };

    private sealed class StubDocumentQueries : IDocumentQueries
    {
        private readonly DocumentStatusDto? _status;

        public StubDocumentQueries(DocumentStatusDto? status) => _status = status;

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

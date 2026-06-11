namespace Liakont.Host.Tests.Unit.Documents;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Xunit;

public sealed class DocumentDetailConsoleQueryServiceTests
{
    private static readonly Guid DocId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetDetailAsync_Should_Return_Null_When_Document_Absent()
    {
        var fake = new FakeDocumentQueries { Document = null };
        var service = new DocumentDetailConsoleQueryService(fake);

        var result = await service.GetDetailAsync(DocId);

        result.Should().BeNull("un document introuvable se traduit par un détail null (page « introuvable »)");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Assemble_Document_Events_And_Archive()
    {
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-001", "Issued"),
            Events = [Event("DocumentDetected", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero))],
            Archive = new ArchiveReferenceDto
            {
                PackagePath = "vault/2026/2026-001.zip",
                PackageHash = "sha256:aaa",
                ChainHash = "sha256:bbb",
                ArchivedUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            },
        };
        var service = new DocumentDetailConsoleQueryService(fake);

        var result = await service.GetDetailAsync(DocId);

        result.Should().NotBeNull();
        result!.Document.DocumentNumber.Should().Be("2026-001");
        result.Events.Should().ContainSingle();
        result.Archive.Should().NotBeNull();
        result.IsArchived.Should().BeTrue("une référence d'archive existe");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Expose_Blocking_Reason_Only_When_Blocked()
    {
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-002", "Blocked"),
            Events =
            [
                Event("DocumentDetected", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)),
                Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Le SIREN de l'émetteur est invalide."),
            ],
        };
        var service = new DocumentDetailConsoleQueryService(fake);

        var result = await service.GetDetailAsync(DocId);

        result!.BlockingReason.Should().Be("Le SIREN de l'émetteur est invalide.");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Hide_Stale_Blocking_Reason_When_Not_Blocked()
    {
        // Le document a été bloqué PUIS débloqué/émis : le motif de blocage est périmé et ne doit pas
        // s'afficher (message opérateur trompeur, CLAUDE.md n°12) — l'historique complet reste dans Events.
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-003", "Issued"),
            Events =
            [
                Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Régime TVA non mappé."),
                Event("DocumentIssued", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)),
            ],
        };
        var service = new DocumentDetailConsoleQueryService(fake);

        var result = await service.GetDetailAsync(DocId);

        result!.BlockingReason.Should().BeNull("le motif est périmé sur un document émis");
        result.Events.Should().HaveCount(2, "l'historique complet reste disponible");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Pick_The_Latest_Blocking_Event()
    {
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-004", "Blocked"),
            Events =
            [
                Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Premier motif."),
                Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), detail: "Motif le plus récent."),
            ],
        };
        var service = new DocumentDetailConsoleQueryService(fake);

        var result = await service.GetDetailAsync(DocId);

        result!.BlockingReason.Should().Be("Motif le plus récent.");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Prefer_The_Latest_Recheck_Reason_Over_The_Original_Block()
    {
        // FIX02 : après une re-vérification restée bloquée, le motif COURANT affiché est le dernier évalué
        // (événement DocumentRecheckedStillBlocked), jamais le motif initial périmé.
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-006", "Blocked"),
            Events =
            [
                Event("DocumentBlocked", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), detail: "Motif initial (table TVA absente)."),
                Event("DocumentRecheckedStillBlocked", new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), detail: "Motif réévalué (acheteur professionnel)."),
            ],
        };
        var service = new DocumentDetailConsoleQueryService(fake);

        var result = await service.GetDetailAsync(DocId);

        result!.BlockingReason.Should().Be("Motif réévalué (acheteur professionnel).", "le motif courant = le dernier événement porteur d'un motif (recheck inclus), pas le motif initial périmé");
    }

    [Fact]
    public async Task GetDetailAsync_Should_Report_Not_Archived_When_No_Archive_Reference()
    {
        var fake = new FakeDocumentQueries
        {
            Document = Doc("2026-005", "Detected"),
            Events = [],
            Archive = null,
        };
        var service = new DocumentDetailConsoleQueryService(fake);

        var result = await service.GetDetailAsync(DocId);

        result!.IsArchived.Should().BeFalse();
        result.Archive.Should().BeNull();
    }

    private static DocumentDto Doc(string number, string state) => new()
    {
        Id = DocId,
        SourceReference = $"src/{number}",
        DocumentNumber = number,
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 6, 1),
        SupplierSiren = "123456782",
        CustomerName = "DUPONT J.",
        CustomerIsCompanyHint = false,
        TotalNet = 1000m,
        TotalTax = 162.80m,
        TotalGross = 1162.80m,
        State = state,
        PayloadHash = "sha256:payload",
        FirstSeenUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
    };

    private static DocumentEventDto Event(string type, DateTimeOffset when, string? detail = null) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = DocId,
        TimestampUtc = when,
        EventType = type,
        Detail = detail,
    };

    // Fake configurable d'IDocumentQueries : seules les 3 lectures du détail sont implémentées ; le reste
    // n'est jamais appelé par le service (NotSupported documente le contrat réellement utilisé).
    private sealed class FakeDocumentQueries : IDocumentQueries
    {
        public DocumentDto? Document { get; init; }

        public IReadOnlyList<DocumentEventDto> Events { get; init; } = [];

        public ArchiveReferenceDto? Archive { get; init; }

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Document);

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Events);

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Archive);

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

namespace Liakont.Host.Tests.Unit.Documents;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class DocumentConsoleQueryServiceTests
{
    [Fact]
    public async Task GetDocumentsInPeriodAsync_Should_Return_All_When_Single_Page()
    {
        var fake = new PagingDocumentQueries(BuildDocuments(50));
        var service = new DocumentConsoleQueryService(fake, NullLogger<DocumentConsoleQueryService>.Instance);

        var result = await service.GetDocumentsInPeriodAsync(null, null);

        result.Should().HaveCount(50);
        fake.Calls.Should().Be(1, "tout tient sur une page, aucune pagination superflue");
    }

    [Fact]
    public async Task GetDocumentsInPeriodAsync_Should_Page_Through_Until_Complete_Without_Truncation()
    {
        // 450 documents, plafond serveur 200 → 3 pages (200 + 200 + 50). Aucune troncature silencieuse.
        var documents = BuildDocuments(450);
        var fake = new PagingDocumentQueries(documents);
        var service = new DocumentConsoleQueryService(fake, NullLogger<DocumentConsoleQueryService>.Instance);

        var result = await service.GetDocumentsInPeriodAsync(null, null);

        result.Should().HaveCount(450);
        result.Select(d => d.DocumentNumber).Should().Equal(documents.Select(d => d.DocumentNumber));
        fake.Calls.Should().Be(3);
        fake.RequestedPages.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public async Task GetDocumentsInPeriodAsync_Should_Forward_The_Period_Bounds()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 30);
        var fake = new PagingDocumentQueries(BuildDocuments(10));
        var service = new DocumentConsoleQueryService(fake, NullLogger<DocumentConsoleQueryService>.Instance);

        await service.GetDocumentsInPeriodAsync(from, to);

        fake.LastFilter!.From.Should().Be(from);
        fake.LastFilter!.To.Should().Be(to);
    }

    [Fact]
    public async Task GetDocumentsInPeriodAsync_Should_Stop_Cleanly_When_Data_Shrinks_Mid_Read()
    {
        // TotalCount annonce 450 mais une page renvoie vide (lecture concurrente d'un état mouvant) :
        // le service s'arrête proprement, sans boucle infinie.
        var fake = new ShrinkingDocumentQueries(announcedTotal: 450, realDocuments: BuildDocuments(200));
        var logger = new CapturingLogger();
        var service = new DocumentConsoleQueryService(fake, logger);

        var result = await service.GetDocumentsInPeriodAsync(null, null);

        result.Should().HaveCount(200);
        fake.Calls.Should().BeLessThanOrEqualTo(3);

        // L'incomplétude (arrêt anticipé avec moins de documents que le total annoncé) est TRACÉE.
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("stopped early");
    }

    private static List<DocumentSummaryDto> BuildDocuments(int count)
    {
        var list = new List<DocumentSummaryDto>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new DocumentSummaryDto
            {
                Id = Guid.NewGuid(),
                DocumentNumber = $"DOC-{i:D5}",
                DocumentType = i % 2 == 0 ? "invoice" : "credit_note",
                IssueDate = new DateOnly(2026, 6, 1),
                CustomerName = $"Client {i}",
                TotalGross = 100m + i,
                State = "Issued",
                LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            });
        }

        return list;
    }

    // Fake qui imite PostgresDocumentQueries : pagination 1-basée, plafond 200, tri stable.
    private sealed class PagingDocumentQueries : IDocumentQueries
    {
        private const int MaxPageSize = 200;
        private readonly IReadOnlyList<DocumentSummaryDto> _documents;

        public PagingDocumentQueries(IReadOnlyList<DocumentSummaryDto> documents) => _documents = documents;

        public int Calls { get; private set; }

        public List<int> RequestedPages { get; } = [];

        public DocumentListFilter? LastFilter { get; private set; }

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastFilter = filter;
            RequestedPages.Add(filter.Page);

            var pageSize = Math.Min(filter.PageSize, MaxPageSize);
            var offset = (filter.Page - 1) * pageSize;
            var items = _documents.Skip(offset).Take(pageSize).ToList();

            return Task.FromResult(new DocumentListResult
            {
                Items = items,
                Page = filter.Page,
                PageSize = pageSize,
                TotalCount = _documents.Count,
                CountsByState = new Dictionary<string, int>(),
            });
        }

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Fake qui annonce un total supérieur aux données réellement disponibles (page vide en cours de route).
    private sealed class ShrinkingDocumentQueries : IDocumentQueries
    {
        private const int MaxPageSize = 200;
        private readonly int _announcedTotal;
        private readonly IReadOnlyList<DocumentSummaryDto> _documents;

        public ShrinkingDocumentQueries(int announcedTotal, IReadOnlyList<DocumentSummaryDto> realDocuments)
        {
            _announcedTotal = announcedTotal;
            _documents = realDocuments;
        }

        public int Calls { get; private set; }

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default)
        {
            Calls++;
            var pageSize = Math.Min(filter.PageSize, MaxPageSize);
            var offset = (filter.Page - 1) * pageSize;
            var items = _documents.Skip(offset).Take(pageSize).ToList();

            return Task.FromResult(new DocumentListResult
            {
                Items = items,
                Page = filter.Page,
                PageSize = pageSize,
                TotalCount = _announcedTotal,
                CountsByState = new Dictionary<string, int>(),
            });
        }

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Capture les messages de niveau Warning pour vérifier que l'incomplétude est tracée.
    private sealed class CapturingLogger : ILogger<DocumentConsoleQueryService>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}

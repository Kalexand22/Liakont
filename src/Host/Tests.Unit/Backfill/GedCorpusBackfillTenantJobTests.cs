namespace Liakont.Host.Tests.Unit.Backfill;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Backfill;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Ged.Contracts.Backfill;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Jobs;
using Xunit;

/// <summary>
/// Orchestration du backfill rétroactif GED côté COMPOSITION ROOT (GED10) avec des doubles : le job énumère la chaîne
/// d'archives fiscales, lit chaque document et remet une requête PLATE au point d'entrée GED. Vérifie : (1) chaque
/// entrée dont le document existe est backfillée avec les bons soft-links + champs projetés ; (2) une entrée dont le
/// document est INTROUVABLE est IGNORÉE (jamais deviner un type/des champs, règle 2), pas backfillée.
/// </summary>
public sealed class GedCorpusBackfillTenantJobTests
{
    [Fact]
    public async Task Each_archive_entry_with_a_document_is_backfilled_with_softlinks_and_projected_fields()
    {
        var docId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var store = new FakeArchiveEntryStore(new ArchiveEntryRecord(entryId, docId, "2026/06/FAC-1", "hash-1", "chain-1", DateTimeOffset.UnixEpoch));
        var queries = new FakeDocumentQueries(new Dictionary<Guid, DocumentDto> { [docId] = Document(docId, "facture", "SRC-1", "FAC-1") });
        var backfill = new RecordingBackfill(GedBackfillOutcome.Deferred);

        await new GedCorpusBackfillTenantJob().ExecuteAsync(Context(store, queries, backfill));

        backfill.Requests.Should().ContainSingle();
        var request = backfill.Requests[0];
        request.ArchiveEntryId.Should().Be(entryId);
        request.FiscalDocumentId.Should().Be(docId);
        request.ArchivePath.Should().Be("2026/06/FAC-1", "le chemin du paquet est repris de l'entrée de coffre");
        request.ContentHash.Should().Be("hash-1", "l'empreinte est reprise du coffre, jamais recalculée");
        request.DocumentType.Should().Be("facture");
        request.SourceReference.Should().Be("SRC-1");
        request.SourceFields.Should().ContainKey("documentNumber").WhoseValue.Should().Be("FAC-1");
    }

    [Fact]
    public async Task Archive_entry_without_a_readable_document_is_skipped_not_guessed()
    {
        var missingDocId = Guid.NewGuid();
        var store = new FakeArchiveEntryStore(new ArchiveEntryRecord(Guid.NewGuid(), missingDocId, "2026/06/X", "hash-x", "chain-x", DateTimeOffset.UnixEpoch));
        var queries = new FakeDocumentQueries(new Dictionary<Guid, DocumentDto>()); // aucun document lisible
        var backfill = new RecordingBackfill(GedBackfillOutcome.Deferred);

        await new GedCorpusBackfillTenantJob().ExecuteAsync(Context(store, queries, backfill));

        backfill.Requests.Should().BeEmpty("une entrée sans document lisible est ignorée — on n'invente jamais un type/des champs (règle 2)");
    }

    private static TenantJobContext Context(IArchiveEntryStore store, IDocumentQueries queries, IGedArchivedDocumentBackfill backfill)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(store);
        services.AddSingleton(queries);
        services.AddSingleton(backfill);
        return new TenantJobContext("tenant-x", services.BuildServiceProvider());
    }

    private static DocumentDto Document(Guid id, string documentType, string sourceReference, string documentNumber) => new()
    {
        Id = id,
        SourceReference = sourceReference,
        DocumentNumber = documentNumber,
        DocumentType = documentType,
        IssueDate = new DateOnly(2026, 6, 1),
        CustomerIsCompanyHint = true,
        TotalNet = 100m,
        TotalTax = 20m,
        TotalGross = 120m,
        State = "Archived",
        PayloadHash = "payload-hash",
        FirstSeenUtc = DateTimeOffset.UnixEpoch,
        LastUpdateUtc = DateTimeOffset.UnixEpoch,
    };

    private sealed class FakeArchiveEntryStore : IArchiveEntryStore
    {
        private readonly IReadOnlyList<ArchiveEntryRecord> _chain;

        public FakeArchiveEntryStore(params ArchiveEntryRecord[] chain) => _chain = chain;

        public Task<IReadOnlyList<ArchiveEntryRecord>> GetChainAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_chain);

        public Task<ArchiveEntryRecord> ReserveAsync(Guid documentId, string packagePath, string packageHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Le backfill ne réserve aucune entrée (lecture seule).");
    }

    private sealed class RecordingBackfill : IGedArchivedDocumentBackfill
    {
        private readonly GedBackfillOutcome _outcome;

        public RecordingBackfill(GedBackfillOutcome outcome) => _outcome = outcome;

        public List<GedBackfillDocumentRequest> Requests { get; } = new();

        public Task<GedBackfillOutcome> BackfillAsync(GedBackfillDocumentRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_outcome);
        }
    }

    private sealed class FakeDocumentQueries : IDocumentQueries
    {
        private readonly IReadOnlyDictionary<Guid, DocumentDto> _documents;

        public FakeDocumentQueries(IReadOnlyDictionary<Guid, DocumentDto> documents) => _documents = documents;

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_documents.TryGetValue(id, out var document) ? document : null);

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

        public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

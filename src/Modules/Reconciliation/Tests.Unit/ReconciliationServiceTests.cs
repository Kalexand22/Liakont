namespace Liakont.Modules.Reconciliation.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Reconciliation.Application;
using Liakont.Modules.Reconciliation.Contracts.DTOs;
using Liakont.Modules.Reconciliation.Domain;
using Liakont.Modules.Reconciliation.Tests.Unit.Doubles;
using Xunit;

public sealed class ReconciliationServiceTests
{
    private const string Tenant = "tenant-x";
    private static readonly Guid DocA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakePooledPdfStore _pool = new();
    private readonly FakePdfTextExtractor _extractor = new();
    private readonly FakeDocumentQueries _documents = new();
    private readonly RecordingArchiveService _archive = new();
    private readonly RecordingReconciliationJournal _journal = new();
    private readonly InMemoryReconciliationQueueStore _queue = new();

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    private ReconciliationService CreateService(string? tenant = Tenant) =>
        new(_pool, _extractor, _documents, _archive, _journal, _queue, new StubTenantContext(tenant));

    [Fact]
    public async Task HighConfidence_FileNameMatch_AddsAddendumJournalAndAutoQueueEntry()
    {
        _documents.AddIssued(DocA, "FAC-2026-0042", new DateOnly(2026, 1, 15), 1162.80m);
        _pool.Add("pool-1", "FAC-2026-0042.pdf", Text("contenu scanné"));

        ReconciliationRunResult result = await CreateService().RunForCurrentTenantAsync();

        result.AutoLinked.Should().Be(1);
        _archive.Addenda.Should().ContainSingle().Which.DocumentId.Should().Be(DocA);
        _archive.Addenda[0].Kind.Should().Be("pdf-reconcilie");
        _journal.AutomaticCalls.Should().ContainSingle().Which.Should().Be(DocA);
        _queue.Entries.Should().ContainSingle().Which.Status.Should().Be(ReconciliationStatus.ReconciledAuto);
    }

    [Fact]
    public async Task MediumConfidence_QueuesProposal_WithoutAddendumOrJournal()
    {
        // INV-RECONCILIATION-002 : confiance moyenne ⇒ proposition, JAMAIS de lien automatique.
        _documents.AddIssued(DocA, "FAC-2026-0042", new DateOnly(2026, 1, 15), 1162.80m);
        _pool.Add("pool-2", "document.pdf", Text("émis le 15/01/2026 — total 1162,80 EUR"));

        ReconciliationRunResult result = await CreateService().RunForCurrentTenantAsync();

        result.Proposed.Should().Be(1);
        result.AutoLinked.Should().Be(0);
        _archive.Addenda.Should().BeEmpty();
        _journal.AutomaticCalls.Should().BeEmpty();
        _queue.Entries.Should().ContainSingle().Which.Status.Should().Be(ReconciliationStatus.PendingManual);
    }

    [Fact]
    public async Task NoMatch_QueuesOrphan()
    {
        _documents.AddIssued(DocA, "FAC-2026-0042", new DateOnly(2026, 1, 15), 1162.80m);
        _pool.Add("pool-3", "scan-xyz.pdf", Text("aucune information exploitable"));

        ReconciliationRunResult result = await CreateService().RunForCurrentTenantAsync();

        result.Orphans.Should().Be(1);
        _archive.Addenda.Should().BeEmpty();
        _queue.Entries.Should().ContainSingle().Which.Status.Should().Be(ReconciliationStatus.Orphan);
    }

    [Fact]
    public async Task AlreadyProcessedPdf_IsSkipped()
    {
        _documents.AddIssued(DocA, "FAC-2026-0042", new DateOnly(2026, 1, 15), 1162.80m);
        _pool.Add("pool-1", "FAC-2026-0042.pdf", Text("scan"));
        await _queue.AddAsync(ReconciliationQueueEntry.Orphan("pool-1", "FAC-2026-0042.pdf", "déjà traité", DateTimeOffset.UtcNow));

        ReconciliationRunResult result = await CreateService().RunForCurrentTenantAsync();

        result.Processed.Should().Be(0);
        _archive.Addenda.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmManualReconciliation_AddsAddendumJournalAndResolvesEntry()
    {
        _documents.AddIssued(DocA, "FAC-2026-0099", new DateOnly(2026, 1, 16), 50.00m);
        _pool.Add("pool-9", "scan-libre.pdf", Text("scan sans repère"));
        var orphan = ReconciliationQueueEntry.Orphan("pool-9", "scan-libre.pdf", "orphelin", DateTimeOffset.UtcNow);
        await _queue.AddAsync(orphan);

        await CreateService().ConfirmManualReconciliationAsync(orphan.Id, DocA, "operateur.test");

        _archive.Addenda.Should().ContainSingle().Which.DocumentId.Should().Be(DocA);
        _journal.ManualCalls.Should().ContainSingle().Which.Should().Be((DocA, "operateur.test"));
        orphan.Status.Should().Be(ReconciliationStatus.ReconciledManual);
        orphan.OperatorIdentity.Should().Be("operateur.test");
    }

    [Fact]
    public async Task UnresolvedTenant_Throws()
    {
        ReconciliationService service = CreateService(tenant: null);

        Func<Task> act = () => service.RunForCurrentTenantAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetIssuedDocumentsWithoutPdf_ExcludesReconciledDocuments()
    {
        _documents.AddIssued(DocA, "FAC-A", new DateOnly(2026, 1, 1), 10m);
        var docB = Guid.NewGuid();
        _documents.AddIssued(docB, "FAC-B", new DateOnly(2026, 1, 2), 20m);
        await _queue.AddAsync(ReconciliationQueueEntry.AutoReconciled("p1", "FAC-A.pdf", DocA, MatchStrategy.FileName, "auto", DateTimeOffset.UtcNow));

        IReadOnlyList<DocumentWithoutPdfDto> without = await CreateService().GetIssuedDocumentsWithoutPdfAsync();

        without.Select(d => d.DocumentId).Should().Contain(docB);
        without.Select(d => d.DocumentId).Should().NotContain(DocA);
    }

    [Fact]
    public async Task GetPendingProposals_MapsProposalToDto()
    {
        await _queue.AddAsync(ReconciliationQueueEntry.PendingProposal("p2", "doc.pdf", DocA, "proposition", DateTimeOffset.UtcNow));

        IReadOnlyList<ReconciliationProposalDto> proposals = await CreateService().GetPendingProposalsAsync();

        proposals.Should().ContainSingle().Which.Should().Match<ReconciliationProposalDto>(p =>
            p.ProposedDocumentId == DocA &&
            p.Confidence == "Medium" &&
            p.Strategy == "DateAndAmount");
    }

    [Fact]
    public async Task GetOrphanPdfs_MapsOrphanToDto()
    {
        await _queue.AddAsync(ReconciliationQueueEntry.Orphan("p3", "orphan.pdf", "aucune correspondance", DateTimeOffset.UtcNow));

        IReadOnlyList<OrphanPdfDto> orphans = await CreateService().GetOrphanPdfsAsync();

        orphans.Should().ContainSingle().Which.Should().Match<OrphanPdfDto>(o =>
            o.FileName == "orphan.pdf" &&
            o.PoolPdfId == "p3");
    }
}

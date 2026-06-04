namespace Liakont.Modules.Archive.Tests.Integration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Archive.Tests.Integration.Fixtures;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Tests d'intégration du module Archive sur PostgreSQL réel : persistance dans
/// <c>documents.archive_entries</c>, chaînage, garde WORM (triggers V005) et détection d'altération via le
/// store FileSystem réel. Chaque test tourne sur sa propre base (isolation de la chaîne — fixture).
/// </summary>
[Collection("ArchiveIntegration")]
public sealed class ArchiveChainIntegrationTests : IDisposable
{
    private const string Tenant = "acme";

    private readonly string _archiveRoot;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ArchiveService _service;

    public ArchiveChainIntegrationTests(ArchiveDatabaseFixture fixture)
    {
        _connectionFactory = fixture.CreateTenantDatabase();
        _archiveRoot = Path.Combine(Path.GetTempPath(), "liakont-archive-it", Guid.NewGuid().ToString("N"));

        var store = new FileSystemArchiveStore(Options.Create(new FileSystemArchiveStoreOptions { RootPath = _archiveRoot }));
        var entryStore = new PostgresArchiveEntryStore(_connectionFactory);
        _service = new ArchiveService(store, entryStore, new StubTenantContext(Tenant));
    }

    [Fact]
    public async Task ArchiveIssuedDocument_PersistsEntry_InDocumentsArchiveEntries()
    {
        Guid documentId = await SeedDocumentAsync("F-2026-001");

        ArchivePackageResult result = await _service.ArchiveIssuedDocumentAsync(PackageRequest(documentId, "F-2026-001"));

        using var connection = await _connectionFactory.OpenAsync();
        var row = await connection.QueryFirstAsync(
            "SELECT document_id, package_path, package_hash, chain_hash FROM documents.archive_entries WHERE id = @Id",
            new { Id = result.EntryId });

        ((Guid)row.document_id).Should().Be(documentId);
        ((string)row.package_path).Should().Be("2026/05/F-2026-001/manifest.json");
        ((string)row.package_hash).Should().Be(result.PackageHash);
        ((string)row.chain_hash).Should().Be(result.ChainHash);
    }

    [Fact]
    public async Task PackageThenAddendum_ChainsAndVerifiesIntact()
    {
        Guid documentId = await SeedDocumentAsync("F-2026-001");

        ArchivePackageResult package = await _service.ArchiveIssuedDocumentAsync(PackageRequest(documentId, "F-2026-001"));
        ArchivePackageResult addendum = await _service.AddAddendumAsync(AddendumRequest(documentId, "F-2026-001"));

        addendum.ChainHash.Should().Be(HashChain.Next(package.ChainHash, addendum.PackageHash));

        ArchiveIntegrityReport report = await _service.VerifyTenantChainAsync();
        report.IsIntact.Should().BeTrue();
        report.EntryCount.Should().Be(2);
    }

    [Fact]
    public async Task ArchiveEntry_Update_IsRejectedByWormTrigger()
    {
        Guid documentId = await SeedDocumentAsync("F-2026-001");
        ArchivePackageResult result = await _service.ArchiveIssuedDocumentAsync(PackageRequest(documentId, "F-2026-001"));

        using var connection = await _connectionFactory.OpenAsync();
        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE documents.archive_entries SET package_path = 'altéré' WHERE id = @Id",
            new { Id = result.EntryId });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("WORM");
    }

    [Fact]
    public async Task ArchiveEntry_Delete_IsRejectedByWormTrigger()
    {
        Guid documentId = await SeedDocumentAsync("F-2026-001");
        ArchivePackageResult result = await _service.ArchiveIssuedDocumentAsync(PackageRequest(documentId, "F-2026-001"));

        using var connection = await _connectionFactory.OpenAsync();
        Func<Task> delete = () => connection.ExecuteAsync(
            "DELETE FROM documents.archive_entries WHERE id = @Id",
            new { Id = result.EntryId });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("WORM");
    }

    [Fact]
    public async Task VerifyTenantChain_DetectsFileAlteration_OnRealStore()
    {
        Guid documentId = await SeedDocumentAsync("F-2026-001");
        await _service.ArchiveIssuedDocumentAsync(PackageRequest(documentId, "F-2026-001"));

        // Altération directe sur disque (contourne le produit) : on lève la lecture seule WORM et on réécrit.
        string payloadPath = Path.Combine(_archiveRoot, Tenant, "2026", "05", "F-2026-001", "payload.json");
        File.SetAttributes(payloadPath, FileAttributes.Normal);
        await File.WriteAllBytesAsync(payloadPath, Encoding.UTF8.GetBytes("FAUX"));

        ArchiveIntegrityReport report = await _service.VerifyTenantChainAsync();
        report.IsIntact.Should().BeFalse();
        report.Entries[0].ContentValid.Should().BeFalse();
    }

    [Fact]
    public async Task ArchivedUtc_IsStrictlyIncreasing_AcrossEntries()
    {
        Guid first = await SeedDocumentAsync("F-2026-001");
        Guid second = await SeedDocumentAsync("F-2026-002");

        await _service.ArchiveIssuedDocumentAsync(PackageRequest(first, "F-2026-001"));
        await _service.ArchiveIssuedDocumentAsync(PackageRequest(second, "F-2026-002"));

        using var connection = await _connectionFactory.OpenAsync();
        var timestamps = (await connection.QueryAsync<DateTime>(
            "SELECT archived_utc FROM documents.archive_entries ORDER BY archived_utc ASC, id ASC")).ToList();

        timestamps.Should().HaveCount(2);
        timestamps[1].Should().BeAfter(timestamps[0]);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_archiveRoot))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(_archiveRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_archiveRoot, recursive: true);
    }

    private static ArchivePackageRequest PackageRequest(Guid documentId, string number) => new()
    {
        DocumentId = documentId,
        DocumentNumber = number,
        IssueDate = new DateOnly(2026, 5, 12),
        PayloadJson = """{"number":"F-2026-001"}""",
        PaResponseJson = """{"paDocumentId":"PA-1"}""",
        Readable = new ArchiveReadableDocument(
            number,
            "Facture",
            new DateOnly(2026, 5, 12),
            "EUR",
            "ACME Ventes SARL",
            "123456789",
            "Client Démo",
            new List<ArchiveReadableLine> { new("Service", 1m, 1000m, 1000m, "20 %") },
            new List<ArchiveVatBreakdownLine> { new("20 %", 1000m, 200m) },
            1000m,
            200m,
            1200m),
        PaInvoice = null,
        PaInvoiceAbsenceReason = "La PA ne fournit pas la facture (test).",
        SourceDocument = null,
        SourceDocumentAbsenceReason = "L'adaptateur ne fournit pas le bordereau (test).",
    };

    private static ArchiveAddendumRequest AddendumRequest(Guid documentId, string number) => new()
    {
        DocumentId = documentId,
        DocumentNumber = number,
        IssueDate = new DateOnly(2026, 5, 12),
        Kind = "tax-report",
        Attachment = new ArchiveAttachment("tax-report.xml", "application/xml", Encoding.UTF8.GetBytes("<ledger/>")),
    };

    private async Task<Guid> SeedDocumentAsync(string number)
    {
        var documentId = Guid.NewGuid();
        using var connection = await _connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO documents.documents
                (id, source_reference, document_number, document_type, issue_date,
                 total_net, total_tax, total_gross, state, payload_hash)
            VALUES
                (@Id, @SourceRef, @Number, 'Invoice', DATE '2026-05-12',
                 1000.00, 200.00, 1200.00, 'Issued', @PayloadHash)
            """,
            new
            {
                Id = documentId,
                SourceRef = "SRC-" + number,
                Number = number,
                PayloadHash = "hash-" + number,
            });
        return documentId;
    }
}

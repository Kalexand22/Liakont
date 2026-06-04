namespace Liakont.Modules.Archive.Tests.Integration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
/// Tests d'intégration de l'ancrage temporel (TRK06) sur PostgreSQL réel : table
/// <c>documents.archive_anchors</c> (persistance, garde WORM des triggers V006, idempotence par tête), et
/// vérifieur complet du coffre sur stores réels. Chaque test tourne sur sa propre base (fixture).
/// </summary>
[Collection("ArchiveIntegration")]
public sealed class ArchiveAnchorIntegrationTests : IDisposable
{
    private const string Tenant = "acme";

    private readonly string _archiveRoot;
    private readonly IConnectionFactory _connectionFactory;
    private readonly FileSystemArchiveStore _store;
    private readonly PostgresArchiveEntryStore _entryStore;
    private readonly PostgresArchiveAnchorStore _anchorStore;
    private readonly ArchiveService _archiveService;

    public ArchiveAnchorIntegrationTests(ArchiveDatabaseFixture fixture)
    {
        _connectionFactory = fixture.CreateTenantDatabase();
        _archiveRoot = Path.Combine(Path.GetTempPath(), "liakont-anchor-it", Guid.NewGuid().ToString("N"));
        _store = new FileSystemArchiveStore(Options.Create(new FileSystemArchiveStoreOptions { RootPath = _archiveRoot }));
        _entryStore = new PostgresArchiveEntryStore(_connectionFactory);
        _anchorStore = new PostgresArchiveAnchorStore(_connectionFactory);
        _archiveService = new ArchiveService(_store, _entryStore, new StubTenantContext(Tenant));
    }

    [Fact]
    public async Task Anchor_Append_PersistsAndReadsBack()
    {
        ArchivePackageResult package = await ArchiveDocumentAsync("F-2026-001");

        ArchiveAnchorRecord appended = await _anchorStore.AppendAsync(
            package.EntryId,
            package.ChainHash,
            TimestampAnchorMethod.Rfc3161,
            ArchiveAnchorStatus.Anchored,
            "_anchors/ab/anchor-cd.tsr",
            new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        IReadOnlyList<ArchiveAnchorRecord> all = await _anchorStore.GetAnchorsAsync();

        all.Should().ContainSingle();
        all[0].AnchorId.Should().Be(appended.AnchorId);
        all[0].ChainHeadHash.Should().Be(package.ChainHash);
        all[0].Method.Should().Be(TimestampAnchorMethod.Rfc3161);
        all[0].Status.Should().Be(ArchiveAnchorStatus.Anchored);
    }

    [Fact]
    public async Task AnchorRow_Update_IsRejectedByWormTrigger()
    {
        ArchivePackageResult package = await ArchiveDocumentAsync("F-2026-001");
        ArchiveAnchorRecord appended = await AppendAnchorAsync(package);

        using var connection = await _connectionFactory.OpenAsync();
        Func<Task> update = () => connection.ExecuteAsync(
            "UPDATE documents.archive_anchors SET status = 'pending' WHERE id = @Id",
            new { Id = appended.AnchorId });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("WORM");
    }

    [Fact]
    public async Task AnchorRow_Delete_IsRejectedByWormTrigger()
    {
        ArchivePackageResult package = await ArchiveDocumentAsync("F-2026-001");
        ArchiveAnchorRecord appended = await AppendAnchorAsync(package);

        using var connection = await _connectionFactory.OpenAsync();
        Func<Task> delete = () => connection.ExecuteAsync(
            "DELETE FROM documents.archive_anchors WHERE id = @Id",
            new { Id = appended.AnchorId });

        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("WORM");
    }

    [Fact]
    public async Task GetLatestForHead_ReturnsAnchor_AndNullForUnknown()
    {
        ArchivePackageResult package = await ArchiveDocumentAsync("F-2026-001");
        ArchiveAnchorRecord appended = await AppendAnchorAsync(package);

        ArchiveAnchorRecord? latest = await _anchorStore.GetLatestForHeadAsync(package.ChainHash, TimestampAnchorMethod.Rfc3161);
        ArchiveAnchorRecord? unknown = await _anchorStore.GetLatestForHeadAsync("ffff", TimestampAnchorMethod.Rfc3161);

        latest.Should().NotBeNull();
        latest!.AnchorId.Should().Be(appended.AnchorId);
        unknown.Should().BeNull();
    }

    [Fact]
    public async Task Verify_OverRealStores_FlagsMissingAnchorProof()
    {
        ArchivePackageResult package = await ArchiveDocumentAsync("F-2026-001");

        // Ancrage indexé mais preuve ABSENTE du coffre (le fichier n'est jamais écrit).
        await _anchorStore.AppendAsync(
            package.EntryId,
            package.ChainHash,
            TimestampAnchorMethod.Rfc3161,
            ArchiveAnchorStatus.Anchored,
            "_anchors/ab/anchor-absent.tsr",
            new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        var verifier = new ArchiveVerifier(
            _archiveService,
            _entryStore,
            _anchorStore,
            _store,
            new Rfc3161TimestampAnchor(new ThrowingTsaClient()),
            new StubTenantContext(Tenant));

        ArchiveVerificationReport report = await verifier.VerifyTenantVaultAsync();

        report.Chain.IsIntact.Should().BeTrue();
        report.Anchors.Should().ContainSingle();
        report.Anchors[0].IsValid.Should().BeFalse();
        report.Anchors[0].Detail.Should().Contain("manquante");
        report.IsFullyVerified.Should().BeFalse();
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

    private async Task<ArchiveAnchorRecord> AppendAnchorAsync(ArchivePackageResult package) =>
        await _anchorStore.AppendAsync(
            package.EntryId,
            package.ChainHash,
            TimestampAnchorMethod.Rfc3161,
            ArchiveAnchorStatus.Anchored,
            "_anchors/ab/anchor-cd.tsr",
            new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

    private async Task<ArchivePackageResult> ArchiveDocumentAsync(string number)
    {
        Guid documentId = await SeedDocumentAsync(number);
        return await _archiveService.ArchiveIssuedDocumentAsync(PackageRequest(documentId, number));
    }

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
            new { Id = documentId, SourceRef = "SRC-" + number, Number = number, PayloadHash = "hash-" + number });
        return documentId;
    }

    private sealed class ThrowingTsaClient : ITsaClient
    {
        public Task<byte[]> RequestTokenAsync(byte[] requestDer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("La TSA ne doit pas être appelée pour la vérification d'une preuve absente.");
    }
}

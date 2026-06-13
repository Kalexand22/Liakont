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
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Preuve de bout en bout de la sauvegarde/restauration de l'appliance (OPS01b, F12 §6.2) : « une sauvegarde
/// jamais restaurée est un faux vert ». On archive un coffre (entrées WORM en base + fichiers de paquet sur
/// le volume), on le SAUVEGARDE avec le vrai mécanisme (<c>pg_dump</c> de la base + copie du volume), on
/// RESTAURE dans une instance VIERGE (<c>pg_restore</c> dans une base neuve + volume restitué), puis on exige
/// que le vérifieur du coffre (TRK06) soit VERT sur l'instance restaurée.
///
/// Les deux contrôles négatifs prouvent que la sauvegarde DOIT capturer la base ET le volume : restaurer la
/// base sans le volume rompt la chaîne (fichiers de preuve manquants) ; restaurer le volume sans la base rend
/// un coffre VIDE (toutes les écritures fiscales perdues) — un faux vert « vacuously valid » si l'on ne
/// vérifiait pas aussi le nombre d'entrées.
/// </summary>
[Collection("BackupRestoreIntegration")]
public sealed class BackupRestoreRoundTripTests : IDisposable
{
    private const string Tenant = "acme";

    private readonly BackupRestoreDatabaseFixture _fixture;
    private readonly string _sourceRoot;
    private readonly string _restoredRoot;
    private readonly string _emptyRoot;

    public BackupRestoreRoundTripTests(BackupRestoreDatabaseFixture fixture)
    {
        _fixture = fixture;
        string scope = Guid.NewGuid().ToString("N");
        _sourceRoot = Path.Combine(Path.GetTempPath(), "liakont-backuprestore-it", scope, "source");
        _restoredRoot = Path.Combine(Path.GetTempPath(), "liakont-backuprestore-it", scope, "restored");
        _emptyRoot = Path.Combine(Path.GetTempPath(), "liakont-backuprestore-it", scope, "empty");
    }

    [Fact]
    public async Task FullBackupRestore_IntoVirginInstance_VaultVerifierIsGreen()
    {
        // ── Source : un coffre réel (2 entrées chaînées : base + fichiers de paquet) ──
        string sourceDb = "src_" + Guid.NewGuid().ToString("N");
        IConnectionFactory sourceFactory = _fixture.CreateMigratedDatabase(sourceDb);
        var sourceStore = new FileSystemArchiveStore(Options.Create(new FileSystemArchiveStoreOptions { RootPath = _sourceRoot }));
        await SeedVaultAsync(sourceFactory, sourceStore, "F-2026-001");
        await SeedVaultAsync(sourceFactory, sourceStore, "F-2026-002");

        ArchiveVerificationReport baseline = await VerifyAsync(sourceFactory, sourceStore);
        baseline.IsFullyVerified.Should().BeTrue("le coffre source doit être intègre avant la sauvegarde");
        baseline.Chain.EntryCount.Should().Be(2);

        // ── Sauvegarde : vrai pg_dump de la base + copie du volume du coffre ──
        string dumpPath = "/tmp/" + sourceDb + ".dump";
        await _fixture.ExecOkAsync("pg_dump", "-U", _fixture.SuperUser, "-Fc", "-d", sourceDb, "-f", dumpPath);

        // ── Restauration dans une instance VIERGE : base neuve (pg_restore) + volume restitué ──
        string restoredDb = "restored_" + Guid.NewGuid().ToString("N");
        await _fixture.CreateEmptyDatabaseAsync(restoredDb);
        await _fixture.ExecOkAsync("pg_restore", "-U", _fixture.SuperUser, "-d", restoredDb, dumpPath);
        CopyDirectory(_sourceRoot, _restoredRoot);

        IConnectionFactory restoredFactory = _fixture.ConnectionFactoryFor(restoredDb);
        var restoredStore = new FileSystemArchiveStore(Options.Create(new FileSystemArchiveStoreOptions { RootPath = _restoredRoot }));

        ArchiveVerificationReport restored = await VerifyAsync(restoredFactory, restoredStore);

        restored.IsFullyVerified.Should().BeTrue("l'instance restaurée doit présenter un coffre intègre (TRK06 vert)");
        restored.Chain.IsIntact.Should().BeTrue();
        restored.Chain.EntryCount.Should().Be(2, "les deux entrées scellées doivent survivre à la restauration");
    }

    [Fact]
    public async Task Restore_DatabaseOnly_WithoutVolume_IsDetectedAsBroken()
    {
        // La base est restaurée (entrées présentes) mais le volume du coffre est PERDU : les fichiers de
        // paquet manquent → la chaîne ne peut être recalculée → coffre rompu (le volume DOIT être sauvegardé).
        string sourceDb = "src_" + Guid.NewGuid().ToString("N");
        IConnectionFactory sourceFactory = _fixture.CreateMigratedDatabase(sourceDb);
        var sourceStore = new FileSystemArchiveStore(Options.Create(new FileSystemArchiveStoreOptions { RootPath = _sourceRoot }));
        await SeedVaultAsync(sourceFactory, sourceStore, "F-2026-001");

        Directory.CreateDirectory(_emptyRoot);
        var emptyStore = new FileSystemArchiveStore(Options.Create(new FileSystemArchiveStoreOptions { RootPath = _emptyRoot }));

        ArchiveVerificationReport report = await VerifyAsync(sourceFactory, emptyStore);

        report.IsFullyVerified.Should().BeFalse();
        report.Chain.IsIntact.Should().BeFalse("une pièce de paquet manquante rompt la chaîne");
    }

    [Fact]
    public async Task Restore_VolumeOnly_WithoutDatabase_YieldsEmptyVault()
    {
        // Le volume est restauré (fichiers présents) mais la base ne l'est PAS : aucune entrée scellée →
        // coffre VIDE. Le vérifieur le considère « vacuously valid » : c'est exactement le faux vert que la
        // sauvegarde doit éviter (la base DOIT être sauvegardée). On le démasque par le nombre d'entrées.
        string sourceDb = "src_" + Guid.NewGuid().ToString("N");
        IConnectionFactory sourceFactory = _fixture.CreateMigratedDatabase(sourceDb);
        var sourceStore = new FileSystemArchiveStore(Options.Create(new FileSystemArchiveStoreOptions { RootPath = _sourceRoot }));
        await SeedVaultAsync(sourceFactory, sourceStore, "F-2026-001");
        CopyDirectory(_sourceRoot, _restoredRoot);

        string virginDb = "virgin_" + Guid.NewGuid().ToString("N");
        IConnectionFactory virginFactory = _fixture.CreateMigratedDatabase(virginDb);
        var restoredStore = new FileSystemArchiveStore(Options.Create(new FileSystemArchiveStoreOptions { RootPath = _restoredRoot }));

        ArchiveVerificationReport report = await VerifyAsync(virginFactory, restoredStore);

        report.Chain.EntryCount.Should().Be(0, "sans la base, toutes les écritures fiscales sont perdues");
        report.Summary.Should().Contain("Coffre vide");
    }

    public void Dispose()
    {
        foreach (string root in new[] { _sourceRoot, _restoredRoot, _emptyRoot })
        {
            DeleteTree(root);
        }
    }

    private static async Task<ArchiveVerificationReport> VerifyAsync(IConnectionFactory factory, FileSystemArchiveStore store)
    {
        var entryStore = new PostgresArchiveEntryStore(factory);
        var anchorStore = new PostgresArchiveAnchorStore(factory);
        var archiveService = new ArchiveService(store, entryStore, new StubTenantContext(Tenant));
        var verifier = new ArchiveVerifier(
            archiveService,
            entryStore,
            anchorStore,
            store,
            new NoAnchorTimestampAnchor(),
            new StubTenantContext(Tenant));
        return await verifier.VerifyTenantVaultAsync();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void DeleteTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(root, recursive: true);
    }

    private static async Task SeedVaultAsync(IConnectionFactory factory, FileSystemArchiveStore store, string number)
    {
        var documentId = Guid.NewGuid();
        using (var connection = await factory.OpenAsync())
        {
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
        }

        var archiveService = new ArchiveService(store, new PostgresArchiveEntryStore(factory), new StubTenantContext(Tenant));
        await archiveService.ArchiveIssuedDocumentAsync(PackageRequest(documentId, number), CancellationToken.None);
    }

    private static ArchivePackageRequest PackageRequest(Guid documentId, string number) => new()
    {
        DocumentId = documentId,
        DocumentNumber = number,
        IssueDate = new DateOnly(2026, 5, 12),
        PayloadJson = "{\"number\":\"" + number + "\"}",
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
}

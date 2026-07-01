namespace Liakont.Modules.Archive.Tests.Integration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Archive.Tests.Integration.Fixtures;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Tests d'intégration de la surface d'archivage GÉNÉRIQUE (GED07, F19 §5.1, option C) sur store FileSystem
/// RÉEL + PostgreSQL réel : le document GED est rangé write-once sous <c>_ged/…</c> et NE crée AUCUNE ligne
/// <c>documents.archive_entries</c> (hash-neutralité facture, INV-ARCH-GED-1). Chaque test tourne sur sa propre
/// base (isolation, fixture).
/// </summary>
[Collection("ArchiveIntegration")]
public sealed class GenericArchiveIntegrationTests : IDisposable
{
    private const string Tenant = "acme";

    private readonly string _archiveRoot;
    private readonly IConnectionFactory _connectionFactory;
    private readonly GenericArchiveService _service;

    public GenericArchiveIntegrationTests(ArchiveDatabaseFixture fixture)
    {
        _connectionFactory = fixture.CreateTenantDatabase();
        _archiveRoot = Path.Combine(Path.GetTempPath(), "liakont-ged-archive-it", Guid.NewGuid().ToString("N"));

        var store = new FileSystemArchiveStore(Options.Create(new FileSystemArchiveStoreOptions { RootPath = _archiveRoot }));
        _service = new GenericArchiveService(store, new StubTenantContext(Tenant));
    }

    [Fact]
    public async Task ArchiveManagedDocument_RangesUnderGedPrefix_AndCreatesNoFiscalEntry()
    {
        GedArchivePackageResult result = await _service.ArchiveManagedDocumentAsync(Request());

        // Rangé write-once sous _ged/ dans le coffre RÉEL (fichier sur disque).
        result.ArchivePath.Should().Be("_ged/bordereau/2026/05/K-42/manifest.json");
        string manifestOnDisk = Path.Combine(_archiveRoot, Tenant, "_ged", "bordereau", "2026", "05", "K-42", "manifest.json");
        File.Exists(manifestOnDisk).Should().BeTrue();

        // Option C : AUCUNE ligne de chaîne fiscale pour un document GED-seul (le coffre fiscal n'est pas touché).
        using var connection = await _connectionFactory.OpenAsync();
        long fiscalEntries = await connection.QueryFirstAsync<long>("SELECT count(*) FROM documents.archive_entries");
        fiscalEntries.Should().Be(0);
    }

    [Fact]
    public async Task ArchiveManagedDocument_IsIdempotent_OnReplay()
    {
        GedArchivePackageResult first = await _service.ArchiveManagedDocumentAsync(Request());
        GedArchivePackageResult second = await _service.ArchiveManagedDocumentAsync(Request());

        second.AlreadyArchived.Should().BeTrue();
        second.ArchivePath.Should().Be(first.ArchivePath);
        second.ContentHash.Should().Be(first.ContentHash);

        using var connection = await _connectionFactory.OpenAsync();
        long fiscalEntries = await connection.QueryFirstAsync<long>("SELECT count(*) FROM documents.archive_entries");
        fiscalEntries.Should().Be(0);
    }

    private static GedArchivePackageRequest Request() => new(
        ArchiveKind: "bordereau",
        ArchiveKey: "K-42",
        FiledOn: new DateOnly(2026, 5, 12),
        Contents: new List<ArchiveAttachment> { new("piece.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-ged")) },
        ReadableHtml: "<p>aperçu</p>",
        IndexAxes: new List<ArchiveIndexAxis> { new("numero_lot", "42", IsConfidential: false) });

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
}

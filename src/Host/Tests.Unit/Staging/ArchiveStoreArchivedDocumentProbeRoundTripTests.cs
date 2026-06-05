namespace Liakont.Host.Tests.Unit.Staging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Staging;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Staging.Contracts;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Test de bout en bout (round-trip) : prouve que <c>ArchiveStoreArchivedDocumentProbe</c> interroge
/// EXACTEMENT le chemin que <c>ArchiveService.ArchiveIssuedDocumentAsync</c> scelle dans le coffre
/// (via le vrai <see cref="FileSystemArchiveStore"/>, pas seulement le helper partagé).
/// </summary>
public sealed class ArchiveStoreArchivedDocumentProbeRoundTripTests
{
    [Fact]
    public async Task IsArchivedAsync_True_After_ArchiveService_Writes_Manifest()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "liakont-staging-rt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemArchiveStore(
                Options.Create(new FileSystemArchiveStoreOptions { RootPath = tempDir }));

            var entryStore = new InlineArchiveEntryStore();
            var tenantContext = new RoundTripFakeTenantContext("tenant-a");
            var archiveService = new ArchiveService(store, entryStore, tenantContext);

            Guid documentId = Guid.NewGuid();
            var request = new ArchivePackageRequest
            {
                DocumentId = documentId,
                DocumentNumber = "INV-RT-001",
                IssueDate = new DateOnly(2026, 6, 15),
                PayloadJson = """{"number":"INV-RT-001","total":1200.00}""",
                PaResponseJson = """{"paDocumentId":"PA-RT-1","ledgerId":"DGFIP-RT"}""",
                Readable = new ArchiveReadableDocument(
                    DocumentNumber: "INV-RT-001",
                    DocumentTypeLabel: "Facture",
                    IssueDate: new DateOnly(2026, 6, 15),
                    CurrencyCode: "EUR",
                    SellerName: "Vendeur Test SARL",
                    SellerSiren: null,
                    BuyerName: "Acheteur Test",
                    Lines: new List<ArchiveReadableLine>
                    {
                        new("Prestation test", 1m, 1000.00m, 1000.00m, "20 %"),
                    },
                    VatBreakdown: new List<ArchiveVatBreakdownLine>
                    {
                        new("20 %", 1000.00m, 200.00m),
                    },
                    TotalNet: 1000.00m,
                    TotalTax: 200.00m,
                    TotalGross: 1200.00m),
                PaInvoice = null,
                PaInvoiceAbsenceReason = "motif test",
                SourceDocument = null,
                SourceDocumentAbsenceReason = "motif test",
            };

            await archiveService.ArchiveIssuedDocumentAsync(request);

            var probe = new ArchiveStoreArchivedDocumentProbe(store, tenantContext);
            var locatorFound = new ArchivedDocumentLocator(documentId, 2026, 6, "INV-RT-001");
            var locatorAbsent = new ArchivedDocumentLocator(documentId, 2026, 6, "INV-RT-999");

            bool archived = await probe.IsArchivedAsync(locatorFound);
            bool absent = await probe.IsArchivedAsync(locatorAbsent);

            archived.Should().BeTrue("la sonde doit trouver le manifest réellement écrit par ArchiveService");
            absent.Should().BeFalse("un numéro différent pointe vers un chemin absent");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    RemoveReadOnlyAttributes(tempDir);
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Nettoyage best-effort : ne fait pas échouer le test.
                }
            }
        }
    }

    private static void RemoveReadOnlyAttributes(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            FileAttributes attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }
    }

    private sealed class InlineArchiveEntryStore : IArchiveEntryStore
    {
        private readonly List<ArchiveEntryRecord> _records = [];

        public Task<ArchiveEntryRecord> ReserveAsync(
            Guid documentId,
            string packagePath,
            string packageHash,
            CancellationToken cancellationToken = default)
        {
            ArchiveEntryRecord? existing = _records.Find(r => r.PackagePath == packagePath);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            string? previousChain = _records.Count == 0 ? null : _records[^1].ChainHash;
            DateTimeOffset archivedUtc = _records.Count == 0
                ? DateTimeOffset.UnixEpoch
                : _records[^1].ArchivedUtc.AddTicks(10);
            string chainHash = HashChain.Next(previousChain, packageHash);
            var record = new ArchiveEntryRecord(Guid.NewGuid(), documentId, packagePath, packageHash, chainHash, archivedUtc);
            _records.Add(record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<ArchiveEntryRecord>> GetChainAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArchiveEntryRecord>>(_records.ToArray());
    }

    private sealed class RoundTripFakeTenantContext : ITenantContext
    {
        public RoundTripFakeTenantContext(string tenantId)
        {
            TenantId = tenantId;
        }

        public string? TenantId { get; }

        public bool IsResolved => !string.IsNullOrWhiteSpace(TenantId);
    }
}

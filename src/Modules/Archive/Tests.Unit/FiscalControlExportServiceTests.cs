namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Tests.Unit.Doubles;
using Liakont.Modules.Documents.Contracts.DTOs;
using Xunit;

/// <summary>Tests de l'export contrôle fiscal (TRK06) : assemblage du dossier, notice, filtre de période.</summary>
public sealed class FiscalControlExportServiceTests
{
    private readonly InMemoryArchiveStore _store = new();
    private readonly FakeArchiveEntryStore _entryStore = new();
    private readonly FakeArchiveAnchorStore _anchorStore = new();
    private readonly FakeDocumentQueries _documentQueries = new();
    private readonly StubTenantContext _tenant = new(ArchiveTestData.Tenant);
    private readonly ArchiveService _archiveService;

    public FiscalControlExportServiceTests()
    {
        _archiveService = new ArchiveService(_store, _entryStore, _tenant);
    }

    private static DocumentDto BuildDoc(Guid id, string number) => new()
    {
        Id = id,
        SourceReference = "SRC-" + number,
        DocumentNumber = number,
        DocumentType = "Invoice",
        IssueDate = new DateOnly(2026, 5, 12),
        SupplierSiren = "123456789",
        CustomerName = "Client Démo",
        CustomerIsCompanyHint = true,
        TotalNet = 1000m,
        TotalTax = 200m,
        TotalGross = 1200m,
        State = "Issued",
        PayloadHash = "hash-" + number,
        FirstSeenUtc = DateTimeOffset.UnixEpoch,
        LastUpdateUtc = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task BuildForDocument_AssemblesDossier()
    {
        ArchivePackageRequest request = ArchiveTestData.PackageRequest();
        await _archiveService.ArchiveIssuedDocumentAsync(request);
        _documentQueries.Add(
            BuildDoc(request.DocumentId, request.DocumentNumber),
            new DocumentEventDto
            {
                Id = Guid.NewGuid(),
                DocumentId = request.DocumentId,
                TimestampUtc = DateTimeOffset.UnixEpoch,
                EventType = "Issued",
                Detail = "émis",
            });

        FiscalControlExport export = await Create().BuildForDocumentAsync(request.DocumentId);

        export.IsComplete.Should().BeTrue();
        var paths = export.Files.Select(f => f.Path).ToList();
        paths.Should().Contain("2026/05/F-2026-001/manifest.json");
        paths.Should().Contain("2026/05/F-2026-001/payload.json");
        paths.Should().Contain("2026/05/F-2026-001/chronologie.json");
        paths.Should().Contain("2026/05/F-2026-001/chronologie.txt");
        paths.Should().Contain("rapport-integrite.json");
        paths.Should().Contain("notice-verification.txt");
        export.Notice.Should().Contain("NF Z42-013");
    }

    [Fact]
    public async Task BuildForDocument_Unknown_IsNotComplete_ButHasNotice()
    {
        FiscalControlExport export = await Create().BuildForDocumentAsync(Guid.NewGuid());

        export.IsComplete.Should().BeFalse();
        export.Files.Select(f => f.Path).Should().Contain("notice-verification.txt");
    }

    [Fact]
    public async Task BuildForPeriod_FiltersByMonth()
    {
        ArchivePackageRequest request = ArchiveTestData.PackageRequest();
        await _archiveService.ArchiveIssuedDocumentAsync(request);
        _documentQueries.Add(BuildDoc(request.DocumentId, request.DocumentNumber));

        FiscalControlExport may = await Create().BuildForPeriodAsync(2026, 5);
        FiscalControlExport june = await Create().BuildForPeriodAsync(2026, 6);

        may.IsComplete.Should().BeTrue();
        june.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task BuildForRange_FiltersByMonth()
    {
        ArchivePackageRequest request = ArchiveTestData.PackageRequest();
        await _archiveService.ArchiveIssuedDocumentAsync(request);
        _documentQueries.Add(BuildDoc(request.DocumentId, request.DocumentNumber));

        FiscalControlExport inRange = await Create().BuildForRangeAsync(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        FiscalControlExport outOfRange = await Create().BuildForRangeAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        inRange.IsComplete.Should().BeTrue();
        outOfRange.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task BuildForRange_PartialMonth_StillSelectsWholeMonth()
    {
        // Le coffre est partitionné par mois : un jour de début à l'intérieur du mois retient tout le mois.
        ArchivePackageRequest request = ArchiveTestData.PackageRequest();
        await _archiveService.ArchiveIssuedDocumentAsync(request);
        _documentQueries.Add(BuildDoc(request.DocumentId, request.DocumentNumber));

        FiscalControlExport export = await Create().BuildForRangeAsync(new DateOnly(2026, 5, 20), new DateOnly(2026, 5, 25));

        export.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task BuildForRange_NullBounds_ReturnsWholeVault()
    {
        ArchivePackageRequest request = ArchiveTestData.PackageRequest();
        await _archiveService.ArchiveIssuedDocumentAsync(request);
        _documentQueries.Add(BuildDoc(request.DocumentId, request.DocumentNumber));

        FiscalControlExport export = await Create().BuildForRangeAsync(null, null);

        export.IsComplete.Should().BeTrue();
        export.Files.Select(f => f.Path).Should().Contain("2026/05/F-2026-001/manifest.json");
    }

    [Fact]
    public async Task BuildForRange_InvertedBounds_Throws()
    {
        await Create()
            .Invoking(s => s.BuildForRangeAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 1)))
            .Should().ThrowAsync<ArgumentException>();
    }

    private FiscalControlExportService Create()
    {
        var verifier = new ArchiveVerifier(_archiveService, _entryStore, _anchorStore, _store, new NoAnchorTimestampAnchor(), _tenant);
        return new FiscalControlExportService(_store, _entryStore, verifier, _documentQueries, _tenant);
    }
}

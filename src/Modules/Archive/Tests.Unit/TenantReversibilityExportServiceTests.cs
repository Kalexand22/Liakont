namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Tests.Unit.Doubles;
using Liakont.Modules.Documents.Contracts.DTOs;
using Xunit;

/// <summary>
/// Tests du dossier de réversibilité du tenant (API03, F12 §6.3) : agrégation tracking + archive +
/// paramétrage + journal, masquage des secrets, demande du coffre ENTIER, garde tenant.
/// </summary>
public sealed class TenantReversibilityExportServiceTests
{
    private readonly FakeFiscalControlExportService _fiscalExport = new();
    private readonly FakeDocumentQueries _documentQueries = new();
    private readonly FakeTenantSettingsQueries _settings = new();
    private readonly FakeTvaMappingQueries _tva = new();
    private readonly FakeAuditQueries _audit = new();

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
    public async Task Build_AssemblesAllSections()
    {
        Guid docId = Guid.NewGuid();
        _documentQueries.Add(
            BuildDoc(docId, "F-2026-001"),
            new DocumentEventDto
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                TimestampUtc = DateTimeOffset.UnixEpoch,
                EventType = "Issued",
                Detail = "émis",
            });

        TenantReversibilityExport export = await Create().BuildAsync();

        var paths = export.Files.Select(f => f.Path).ToList();
        paths.Should().Contain("archive/2026/05/F-2026-001/manifest.json");
        paths.Should().Contain("archive/rapport-integrite.json");
        paths.Should().Contain("tracking/documents.json");
        paths.Should().Contain("parametrage/profil.json");
        paths.Should().Contain("parametrage/comptes-pa.json");
        paths.Should().Contain("parametrage/table-tva.json");
        paths.Should().Contain("parametrage/planification.json");
        paths.Should().Contain("parametrage/seuils-alerte.json");
        paths.Should().Contain("journal/audit.json");
        paths.Should().Contain("rapport-integrite.json");
        paths.Should().Contain("notice-reversibilite.txt");
        export.Notice.Should().Contain("RÉVERSIBILITÉ");
    }

    [Fact]
    public async Task Build_MasksPaSecrets()
    {
        TenantReversibilityExport export = await Create().BuildAsync();

        FiscalExportFile pa = export.Files.Single(f => f.Path == "parametrage/comptes-pa.json");
        string json = Encoding.UTF8.GetString(pa.Content);

        json.Should().Contain("HasApiKey");

        // Le DTO ne porte AUCUN champ de clé : aucune clé ne peut fuiter (INV-TENANTSETTINGS-003).
        json.Should().NotContain("ApiKeyCipher");
        json.Should().NotContain("\"ApiKey\"");
    }

    [Fact]
    public async Task Build_TrackingDumpsAllDocuments()
    {
        _documentQueries.Add(BuildDoc(Guid.NewGuid(), "F-2026-001"));
        _documentQueries.Add(BuildDoc(Guid.NewGuid(), "F-2026-002"));

        TenantReversibilityExport export = await Create().BuildAsync();

        FiscalExportFile tracking = export.Files.Single(f => f.Path == "tracking/documents.json");
        string json = Encoding.UTF8.GetString(tracking.Content);
        json.Should().Contain("F-2026-001");
        json.Should().Contain("F-2026-002");
    }

    [Fact]
    public async Task Build_RequestsWholeVault()
    {
        await Create().BuildAsync();

        _fiscalExport.RangeCalled.Should().BeTrue();
        _fiscalExport.LastRangeFrom.Should().BeNull();
        _fiscalExport.LastRangeTo.Should().BeNull();
    }

    [Fact]
    public async Task Build_AuditJournal_DocumentsTheCap()
    {
        TenantReversibilityExport export = await Create().BuildAsync();

        FiscalExportFile journal = export.Files.Single(f => f.Path == "journal/audit.json");
        Encoding.UTF8.GetString(journal.Content).Should().Contain("500");
    }

    [Fact]
    public async Task Build_Throws_WhenTenantNotResolved()
    {
        var service = new TenantReversibilityExportService(
            _fiscalExport, _documentQueries, _settings, _tva, _audit, new StubTenantContext(null));

        await service.Invoking(s => s.BuildAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    private TenantReversibilityExportService Create() =>
        new(_fiscalExport, _documentQueries, _settings, _tva, _audit, new StubTenantContext("acme"));
}

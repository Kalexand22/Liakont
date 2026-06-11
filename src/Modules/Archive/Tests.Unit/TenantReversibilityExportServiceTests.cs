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

    /// <summary>Concatène le contenu de tous les fichiers de page du tracking (tracking/documents-NNNN.json).</summary>
    private static string TrackingDocumentsText(TenantReversibilityExport export) =>
        string.Concat(export.Files
            .Where(f => f.Path.StartsWith("tracking/documents-", StringComparison.Ordinal))
            .Select(f => Encoding.UTF8.GetString(f.Content)));

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
        paths.Should().Contain("archive/rapport-integrite.json", "le rapport d'intégrité du coffre est dans la section archive/");
        paths.Should().Contain("tracking/index.json");
        paths.Should().Contain("tracking/documents-0001.json");
        paths.Should().Contain("parametrage/profil.json");
        paths.Should().Contain("parametrage/comptes-pa.json");
        paths.Should().Contain("parametrage/table-tva.json");
        paths.Should().Contain("parametrage/planification.json");
        paths.Should().Contain("parametrage/seuils-alerte.json");
        paths.Should().Contain("journal/audit.json");
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

        string tracking = TrackingDocumentsText(export);
        tracking.Should().Contain("F-2026-001");
        tracking.Should().Contain("F-2026-002");
    }

    [Fact]
    public async Task Build_TrackingPaginatesBeyondOnePage()
    {
        // Plus d'une page (TrackingPageSize=200) : valide l'avance de la boucle (page++ / scanned >=
        // TotalCount) sans perte ni doublon — la logique sujette aux off-by-one. Le tracking est émis par
        // lots (un fichier par page) : on agrège tous les fichiers de page.
        const int count = 201;
        for (int i = 1; i <= count; i++)
        {
            _documentQueries.Add(BuildDoc(Guid.NewGuid(), $"DOC-{i:D3}"));
        }

        TenantReversibilityExport export = await Create().BuildAsync();

        export.Files.Select(f => f.Path).Should().Contain("tracking/documents-0001.json");
        export.Files.Select(f => f.Path).Should().Contain("tracking/documents-0002.json", "201 > 200 ⇒ une seconde page");

        string tracking = TrackingDocumentsText(export);
        tracking.Should().Contain("DOC-001", "le premier document (page 1) est présent");
        tracking.Should().Contain($"DOC-{count:D3}", "le dernier document (page 2) est présent");

        FiscalExportFile index = export.Files.Single(f => f.Path == "tracking/index.json");
        Encoding.UTF8.GetString(index.Content).Should().Contain($"\"totalDocuments\": {count}");
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

    [Fact]
    public async Task Build_Notice_CarriesInstanceBrand_AndPoweredByMention()
    {
        // Marque grise (BRD01) : la notice porte le nom commercial de l'éditeur + la mention « propulsé ».
        TenantReversibilityExport export = await Create(new ReversibilityBranding("Acme Conformité", PoweredByLiakont: true)).BuildAsync();

        export.Notice.Should().Contain("Acme Conformité");
        export.Notice.Should().Contain("Plateforme propulsée par Liakont.");
    }

    [Fact]
    public async Task Build_Notice_HidesPoweredBy_WhenDisabled()
    {
        // L'éditeur masque la marque Liakont : aucune mention « Liakont » ne doit subsister dans la notice.
        TenantReversibilityExport export = await Create(new ReversibilityBranding("Acme Conformité", PoweredByLiakont: false)).BuildAsync();

        export.Notice.Should().Contain("Acme Conformité");
        export.Notice.Should().NotContain("Liakont");
    }

    private TenantReversibilityExportService Create() =>
        new(_fiscalExport, _documentQueries, _settings, _tva, _audit, new StubTenantContext("acme"));

    private TenantReversibilityExportService Create(ReversibilityBranding branding) =>
        new(_fiscalExport, _documentQueries, _settings, _tva, _audit, new StubTenantContext("acme"), branding);
}

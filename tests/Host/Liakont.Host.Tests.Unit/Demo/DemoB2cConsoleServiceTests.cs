namespace Liakont.Host.Tests.Unit.Demo;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Demo;
using Liakont.Host.Documents;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Xunit;

/// <summary>
/// Composition EN LECTURE de la démo Essentiel (B2C04) : le service filtre les déclarations 10.3 (type brut
/// <c>DECLARATION</c>), n'interroge le store de liens reporting↔pièces (B2C03) que pour une transmission ÉMISE
/// (le lien n'est gelé qu'à l'émission), porte la défense tenant-scope (companyId du tenant courant), et ne
/// fabrique aucune valeur. Tests avec doubles : aucune base, aucune logique métier exercée hors composition.
/// </summary>
public sealed class DemoB2cConsoleServiceTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Keeps_Only_Declaration_Typed_Documents()
    {
        var declaration = Summary("DECLARATION", "Blocked");
        var invoice = Summary("F", "Issued");
        var creditNote = Summary("A", "Issued");
        var service = Build(new FakeDocuments(declaration, invoice, creditNote), new FakeLinks(), new FakeTenant(CompanyId));

        var model = await service.GetAsync();

        model.Declarations.Should().ContainSingle();
        model.Declarations[0].Id.Should().Be(declaration.Id, "seules les déclarations 10.3 (type brut DECLARATION) sont restituées.");
    }

    [Fact]
    public async Task Queries_The_Link_Store_Only_For_Issued_Declarations()
    {
        var issued = Summary("DECLARATION", "Issued");
        var blocked = Summary("DECLARATION", "Blocked");
        var links = new FakeLinks();
        links.Seed(CompanyId, issued.Id, "ba-1");
        var service = Build(new FakeDocuments(issued, blocked), links, new FakeTenant(CompanyId));

        var model = await service.GetAsync();

        // Le lien n'est gelé qu'à l'émission : interrogé uniquement pour l'Issued (jamais pour le Blocked).
        links.QueriedDocumentIds.Should().BeEquivalentTo(new[] { issued.Id });
        model.Declarations.Single(d => d.Id == issued.Id).HasReportingLink.Should().BeTrue();
        model.Declarations.Single(d => d.Id == blocked.Id).HasReportingLink.Should().BeFalse();
    }

    [Fact]
    public async Task Issued_Declaration_Without_A_Frozen_Link_Reports_No_Link()
    {
        var issued = Summary("DECLARATION", "Issued");
        var service = Build(new FakeDocuments(issued), new FakeLinks(), new FakeTenant(CompanyId));

        var model = await service.GetAsync();

        model.Declarations.Single().HasReportingLink.Should().BeFalse("aucun lien gelé pour cette transmission.");
    }

    [Fact]
    public async Task Without_A_Resolved_Company_No_Link_Is_Queried()
    {
        var issued = Summary("DECLARATION", "Issued");
        var links = new FakeLinks();
        links.Seed(CompanyId, issued.Id, "ba-1");

        // companyId non résolu (profil tenant absent) : on n'interroge pas le store (défense tenant-scope, n°9).
        var service = Build(new FakeDocuments(issued), links, new FakeTenant(null));

        var model = await service.GetAsync();

        links.QueriedDocumentIds.Should().BeEmpty();
        model.Declarations.Single().HasReportingLink.Should().BeFalse();
    }

    [Fact]
    public async Task Projects_Number_Amount_State_And_Audit_Export_Url()
    {
        var issued = Summary("DECLARATION", "Issued");
        var service = Build(new FakeDocuments(issued), new FakeLinks(), new FakeTenant(CompanyId));

        var row = (await service.GetAsync()).Declarations.Single();

        row.Number.Should().Be(issued.DocumentNumber);
        row.TotalGross.Should().Be(issued.TotalGross);
        row.State.Should().Be("Issued");
        row.AuditExportUrl.Should().Be($"/api/v1/documents/{issued.Id}/audit-export");
    }

    private static DemoB2cConsoleService Build(FakeDocuments documents, FakeLinks links, FakeTenant tenant) =>
        new(documents, links, tenant);

    private static DocumentSummaryDto Summary(string documentType, string state) => new()
    {
        Id = Guid.NewGuid(),
        DocumentNumber = "DOC-" + documentType + "-" + state,
        DocumentType = documentType,
        IssueDate = new DateOnly(2026, 1, 20),
        TotalGross = 144.00m,
        State = state,
        LastUpdateUtc = new DateTimeOffset(2026, 1, 20, 10, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeDocuments : IDocumentConsoleQueries
    {
        private readonly IReadOnlyList<DocumentSummaryDto> _all;

        public FakeDocuments(params DocumentSummaryDto[] all) => _all = all;

        public Task<IReadOnlyList<DocumentSummaryDto>> GetDocumentsInPeriodAsync(
            DateOnly? from, DateOnly? to, string? documentType = null, CancellationToken cancellationToken = default)
        {
            // Double fidèle : le filtre par type est appliqué CÔTÉ « serveur » (comme la vraie requête), pas en
            // mémoire dans le service. Le service doit donc demander le bon type pour ne voir que les déclarations.
            IReadOnlyList<DocumentSummaryDto> result = documentType is null
                ? _all
                : _all.Where(d => string.Equals(d.DocumentType, documentType, StringComparison.Ordinal)).ToList();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeLinks : IReportingPieceLinkStore
    {
        private readonly Dictionary<Guid, List<ReportingPieceLink>> _byDocument = new();

        public List<Guid> QueriedDocumentIds { get; } = new();

        public void Seed(Guid companyId, Guid documentId, string sourceReference) =>
            _byDocument[documentId] = new List<ReportingPieceLink>
            {
                new(Guid.NewGuid(), companyId, documentId, sourceReference, DateTimeOffset.UtcNow),
            };

        public Task<IReadOnlyList<ReportingPieceLink>> AppendAsync(
            Guid companyId, Guid documentId, IReadOnlyCollection<string> sourceReferences, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("La démo ne gèle aucun lien (lecture seule).");

        public Task<IReadOnlyList<ReportingPieceLink>> GetByDocumentAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken = default)
        {
            QueriedDocumentIds.Add(documentId);
            IReadOnlyList<ReportingPieceLink> links = _byDocument.TryGetValue(documentId, out var list)
                ? list
                : Array.Empty<ReportingPieceLink>();
            return Task.FromResult(links);
        }

        public Task<IReadOnlyList<ReportingPieceLink>> GetBySourceReferenceAsync(Guid companyId, string sourceReference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTenant : ITenantSettingsQueries
    {
        private readonly Guid? _companyId;

        public FakeTenant(Guid? companyId) => _companyId = companyId;

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(_companyId);

        public Task<Liakont.Modules.TenantSettings.Contracts.DTOs.TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Liakont.Modules.TenantSettings.Contracts.DTOs.FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Liakont.Modules.TenantSettings.Contracts.DTOs.PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Liakont.Modules.TenantSettings.Contracts.DTOs.ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Liakont.Modules.TenantSettings.Contracts.DTOs.AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default) => throw new NotSupportedException();
    }
}

namespace Liakont.Modules.Validation.Tests.Unit.Rules;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;

public sealed class SupplierIdentityRuleTests
{
    private const string IssuerSiren = "123456782"; // SIREN émetteur fictif, Luhn valide

    [Fact]
    public void Constructor_rejects_null_queries()
    {
        var act = () => new SupplierIdentityRule(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Configured_and_matching_supplier_has_no_issue()
    {
        var companyId = Guid.NewGuid();
        var rule = new SupplierIdentityRule(new FakeTenantSettingsQueries(Profile(companyId, IssuerSiren)));

        var issues = await rule.ValidateAsync(Context(companyId, supplierSiren: IssuerSiren));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Document_without_supplier_siren_is_accepted()
    {
        // Le SIREN émetteur de référence vient du profil tenant : l'absence dans le document n'est pas une anomalie ici.
        var companyId = Guid.NewGuid();
        var rule = new SupplierIdentityRule(new FakeTenantSettingsQueries(Profile(companyId, IssuerSiren)));

        var issues = await rule.ValidateAsync(Context(companyId, supplierSiren: null));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_tenant_profile_is_blocking()
    {
        var companyId = Guid.NewGuid();
        var rule = new SupplierIdentityRule(new FakeTenantSettingsQueries(profile: null));

        var issues = await rule.ValidateAsync(Context(companyId));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(SupplierIdentityRule.SirenMissing);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("2019");
    }

    [Fact]
    public async Task Invalid_issuer_siren_is_blocking()
    {
        var companyId = Guid.NewGuid();
        var rule = new SupplierIdentityRule(new FakeTenantSettingsQueries(Profile(companyId, "123456789"))); // Luhn invalide

        var issues = await rule.ValidateAsync(Context(companyId));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(SupplierIdentityRule.SirenInvalid);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Supplier_siren_mismatch_between_document_and_tenant_is_blocking()
    {
        var companyId = Guid.NewGuid();
        var rule = new SupplierIdentityRule(new FakeTenantSettingsQueries(Profile(companyId, IssuerSiren)));

        // Le document porte un autre SIREN valide ("000000000") que celui du profil tenant.
        var issues = await rule.ValidateAsync(Context(companyId, supplierSiren: "000000000"));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(SupplierIdentityRule.SirenMismatch);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain(IssuerSiren);
        issue.MessageOperateur.Should().Contain("000000000");
    }

    [Fact]
    public async Task Invalid_supplier_siret_in_document_is_blocking()
    {
        var companyId = Guid.NewGuid();
        var rule = new SupplierIdentityRule(new FakeTenantSettingsQueries(Profile(companyId, IssuerSiren)));

        // SIRET émetteur fourni par le document mais à clé de Luhn invalide (F04 §3.1).
        var issues = await rule.ValidateAsync(Context(companyId, supplierSiren: IssuerSiren, supplierSiret: "12345678200001"));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(SupplierIdentityRule.SiretInvalid);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("2019");
    }

    [Fact]
    public async Task Valid_supplier_siret_in_document_has_no_issue()
    {
        var companyId = Guid.NewGuid();
        var rule = new SupplierIdentityRule(new FakeTenantSettingsQueries(Profile(companyId, IssuerSiren)));

        var issues = await rule.ValidateAsync(Context(companyId, supplierSiren: IssuerSiren, supplierSiret: "12345678200002"));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Profile_is_read_scoped_to_the_context_tenant()
    {
        var companyId = Guid.NewGuid();
        var fake = new FakeTenantSettingsQueries(Profile(companyId, IssuerSiren));
        var rule = new SupplierIdentityRule(fake);

        await rule.ValidateAsync(Context(companyId, supplierSiren: IssuerSiren));

        fake.RequestedCompanyId.Should().Be(companyId, "la lecture du profil doit être scopée au tenant du document (CLAUDE.md n°9).");
    }

    private static DocumentValidationContext Context(Guid companyId, string? supplierSiren = null, string? supplierSiret = null)
    {
        var document = new PivotDocumentDto(
            sourceDocumentKind: "BORDEREAU",
            number: "2019",
            issueDate: new DateTime(2024, 1, 15),
            sourceReference: "src-2019",
            supplier: new PivotPartyDto("Étude Fictive SVV", siren: supplierSiren, siret: supplierSiret),
            totals: new PivotTotalsDto(1160.00m, 0m, 1160.00m),
            operationCategory: OperationCategory.LivraisonBiens);
        return new DocumentValidationContext(document, companyId);
    }

    private static TenantProfileDto Profile(Guid companyId, string siren) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        Siren = siren,
        RaisonSociale = "Étude Fictive SVV",
        Street = "1 rue de la Vente",
        PostalCode = "35000",
        City = "Rennes",
        Country = "FR",
        Statut = "Actif",
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class FakeTenantSettingsQueries : ITenantSettingsQueries
    {
        private readonly TenantProfileDto? _profile;

        public FakeTenantSettingsQueries(TenantProfileDto? profile)
        {
            _profile = profile;
        }

        public Guid? RequestedCompanyId { get; private set; }

        public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default)
        {
            RequestedCompanyId = companyId;
            return Task.FromResult(_profile);
        }

        public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => throw new NotSupportedException();
    }
}

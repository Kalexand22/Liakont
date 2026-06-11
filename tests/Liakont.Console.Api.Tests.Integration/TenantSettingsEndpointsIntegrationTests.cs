namespace Liakont.Console.Api.Tests.Integration;

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process de l'endpoint de paramétrage de la console (API01c,
/// <c>GET /api/v1/settings</c>) : permission <c>liakont.read</c> (401/403), composition profil + fiscal
/// + état de la table TVA, masquage des secrets PA (HasApiKey uniquement), exposition des capacités PA
/// déclarées (plug-in chargé) vs indisponibilité (plug-in non chargé), vue vide tant que le tenant n'est
/// pas paramétré, et isolation tenant (A ≠ B). Le harness enregistre le plug-in factice (PAA02) et seede
/// le paramétrage des tenants A et B (voir <see cref="ConsoleApiFactory"/>).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class TenantSettingsEndpointsIntegrationTests
{
    private const string SettingsPath = "/api/v1/settings";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConsoleApiFactory _factory;

    public TenantSettingsEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSettings_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA);
        var response = await client.GetAsync(SettingsPath);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSettings_Without_Read_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.NoPermissionUserId);
        var response = await client.GetAsync(SettingsPath);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSettings_As_Reader_Returns_Profile_Fiscal_And_Tva_State()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var overview = await GetOverviewAsync(client);

        overview.Profile.Should().NotBeNull();
        overview.Profile!.Siren.Should().Be(ConsoleApiFactory.TenantASiren);
        overview.Profile.RaisonSociale.Should().Be(ConsoleApiFactory.TenantARaisonSociale);
        overview.Profile.Statut.Should().Be("Actif");

        overview.FiscalSettings.Should().NotBeNull();
        overview.FiscalSettings!.VatOnDebits.Should().BeTrue();
        overview.FiscalSettings.ReportingFrequency.Should().Be("mensuel");

        overview.TvaMapping.Should().NotBeNull();
        overview.TvaMapping!.MappingVersion.Should().Be(ConsoleApiFactory.TenantATvaVersion);
        overview.TvaMapping.IsValidated.Should().BeTrue();
        overview.TvaMapping.ValidatedBy.Should().Be(ConsoleApiFactory.TenantATvaValidatedBy);
        overview.TvaMapping.RuleCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSettings_Masks_Pa_Secrets_Exposing_Only_HasApiKey()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync(SettingsPath);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        // La clé chiffrée seedée n'apparaît JAMAIS dans la réponse, ni aucune propriété de clé en clair.
        body.Should().NotContain("enc-fake-a", "la clé API (même chiffrée) n'est jamais sérialisée — INV-TENANTSETTINGS-003");
        body.Should().Contain("hasApiKey", "seule l'existence d'une clé est exposée");

        var overview = JsonSerializer.Deserialize<OverviewResponse>(body, JsonOptions)!;
        var fake = overview.PaAccounts.Single(a => a.Account.PluginType == ConsoleApiFactory.FakePluginType);
        fake.Account.HasApiKey.Should().BeTrue("une clé a été saisie pour le compte Fake");

        var unknown = overview.PaAccounts.Single(a => a.Account.PluginType == ConsoleApiFactory.UnregisteredPluginType);
        unknown.Account.HasApiKey.Should().BeFalse("aucune clé n'a été saisie pour le compte inconnu");
    }

    [Fact]
    public async Task GetSettings_Exposes_Declared_Capabilities_For_Registered_Plugin()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var overview = await GetOverviewAsync(client);

        var fake = overview.PaAccounts.Single(a => a.Account.PluginType == ConsoleApiFactory.FakePluginType);
        fake.PluginAvailable.Should().BeTrue();
        fake.Capabilities.Should().NotBeNull();
        fake.Capabilities!.PaName.Should().Be(ConsoleApiFactory.FakeCapabilities.PaName);
        fake.Capabilities.SupportsB2cReporting.Should().Be(ConsoleApiFactory.FakeCapabilities.SupportsB2cReporting);
        fake.Capabilities.SupportsCreditNotes.Should().Be(ConsoleApiFactory.FakeCapabilities.SupportsCreditNotes);
        fake.Capabilities.MaxDocumentsPerRequest.Should().Be(ConsoleApiFactory.FakeCapabilities.MaxDocumentsPerRequest);
    }

    [Fact]
    public async Task GetSettings_Marks_Unregistered_Plugin_As_Unavailable_Without_Inventing_Capabilities()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var overview = await GetOverviewAsync(client);

        var unknown = overview.PaAccounts.Single(a => a.Account.PluginType == ConsoleApiFactory.UnregisteredPluginType);
        unknown.PluginAvailable.Should().BeFalse("aucun plug-in n'est chargé pour ce type de PA");
        unknown.Capabilities.Should().BeNull("on n'invente jamais de capacité pour un plug-in absent");
    }

    [Fact]
    public async Task GetSettings_Returns_Empty_Overview_When_Tenant_Not_Configured()
    {
        // Tenant sans profil (CFG02 non fait) : 200 + vue vide (état transitoire), jamais 404/500.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.ReaderUserId);

        var overview = await GetOverviewAsync(client);

        overview.Profile.Should().BeNull();
        overview.FiscalSettings.Should().BeNull();
        overview.TvaMapping.Should().BeNull();
        overview.PaAccounts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSettings_Is_Tenant_Isolated()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantB, ConsoleApiFactory.ReaderUserId);

        var overview = await GetOverviewAsync(client);

        overview.Profile.Should().NotBeNull();
        overview.Profile!.Siren.Should().Be(ConsoleApiFactory.TenantBSiren, "le tenant B ne voit que son propre profil");
        overview.Profile.Siren.Should().NotBe(ConsoleApiFactory.TenantASiren);
        overview.PaAccounts.Should().BeEmpty("le tenant B n'a aucun compte PA seedé");
        overview.TvaMapping.Should().BeNull("le tenant B n'a aucune table TVA seedée");
    }

    private static async Task<OverviewResponse> GetOverviewAsync(HttpClient client)
    {
        var response = await client.GetAsync(SettingsPath);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var overview = await response.Content.ReadFromJsonAsync<OverviewResponse>(JsonOptions);
        return overview!;
    }

    private sealed record OverviewResponse(
        ProfileResponse? Profile,
        FiscalResponse? FiscalSettings,
        TvaSummaryResponse? TvaMapping,
        List<PaAccountResponse> PaAccounts);

    private sealed record ProfileResponse(string Siren, string RaisonSociale, string Statut);

    private sealed record FiscalResponse(bool? VatOnDebits, string? ReportingFrequency);

    private sealed record TvaSummaryResponse(string MappingVersion, bool IsValidated, string? ValidatedBy, int RuleCount);

    private sealed record PaAccountResponse(PaAccountInner Account, bool PluginAvailable, PaCapsResponse? Capabilities);

    private sealed record PaAccountInner(string PluginType, bool HasApiKey, bool IsActive);

    private sealed record PaCapsResponse(string PaName, bool SupportsB2cReporting, bool SupportsCreditNotes, int? MaxDocumentsPerRequest);
}

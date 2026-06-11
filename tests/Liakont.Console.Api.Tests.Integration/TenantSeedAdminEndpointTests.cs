namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process de l'endpoint d'import de seed d'un tenant (FIX01a,
/// <c>POST /api/v1/admin/tenants/{tenantId}/seed</c>) : garde <c>SystemAdmin</c> (401/403), tenant
/// inconnu (404), et import nominal (200) qui crée le profil du tenant cible (visible via
/// <c>GET /settings</c>) SANS jamais importer de secret (INV-TENANTSETTINGS-007). Cible le tenant VIERGE
/// dédié (<see cref="ConsoleApiFactory.TenantSeed"/>) pour ne polluer aucune autre suite.
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class TenantSeedAdminEndpointTests
{
    private const string SettingsPath = "/api/v1/settings";
    private const string SystemAdminRole = "SystemAdmin";

    private const string ProfileJson = """
        {
          "siren": "123456782",
          "raisonSociale": "Société Fictive de Démonstration",
          "address": { "street": "1 rue de l'Exemple", "postalCode": "35000", "city": "Rennes", "country": "FR" },
          "contactEmailAlerte": "alertes@exemple.test",
          "fiscal": { "vatOnDebits": null, "operationCategory": null, "reportingFrequency": null },
          "schedule": { "hours": ["03:00"], "catchUpOnStart": true },
          "thresholds": { "agentSilentHours": 12 }
        }
        """;

    private const string PaAccountsJson = """
        [
          { "pluginType": "Fake", "environment": "Staging", "accountIdentifiers": "{}", "apiKey": "${PA_API_KEY_FAKE_STAGING}" }
        ]
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConsoleApiFactory _factory;

    public TenantSeedAdminEndpointTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    private static string SeedPath(string tenantId) => $"/api/v1/admin/tenants/{tenantId}/seed";

    [Fact]
    public async Task SeedTenant_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantSeed);

        var response = await client.PostAsJsonAsync(
            SeedPath(ConsoleApiFactory.TenantSeed),
            new { companyId = ConsoleApiFactory.TenantSeedCompanyId, seedDirectoryPath = "/tmp/x" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SeedTenant_Without_SystemAdmin_Role_Returns_403()
    {
        // Utilisateur authentifié MAIS sans le rôle SystemAdmin : la garde RequireRole refuse (403).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantSeed, ConsoleApiFactory.SystemAdminUserId);

        var response = await client.PostAsJsonAsync(
            SeedPath(ConsoleApiFactory.TenantSeed),
            new { companyId = ConsoleApiFactory.TenantSeedCompanyId, seedDirectoryPath = "/tmp/x" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SeedTenant_For_Unknown_Tenant_Returns_404()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantSeed, ConsoleApiFactory.SystemAdminUserId, roles: SystemAdminRole);

        var response = await client.PostAsJsonAsync(
            SeedPath("tenant-inexistant"),
            new { companyId = ConsoleApiFactory.TenantSeedCompanyId, seedDirectoryPath = "/tmp/x" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SeedTenant_With_Missing_CompanyId_Returns_400()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantSeed, ConsoleApiFactory.SystemAdminUserId, roles: SystemAdminRole);

        var response = await client.PostAsJsonAsync(
            SeedPath(ConsoleApiFactory.TenantSeed),
            new { companyId = Guid.Empty, seedDirectoryPath = "/tmp/x" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SeedTenant_As_SystemAdmin_Imports_Profile_Visible_In_Settings_Without_Secret()
    {
        var dir = CreateSeedDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "tenant-profile.json"), ProfileJson);
            await File.WriteAllTextAsync(Path.Combine(dir, "pa-accounts.json"), PaAccountsJson);

            using var admin = _factory.CreateClient(ConsoleApiFactory.TenantSeed, ConsoleApiFactory.SystemAdminUserId, roles: SystemAdminRole);

            var seedResponse = await admin.PostAsJsonAsync(
                SeedPath(ConsoleApiFactory.TenantSeed),
                new { companyId = ConsoleApiFactory.TenantSeedCompanyId, seedDirectoryPath = dir });

            seedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var seedBody = await seedResponse.Content.ReadFromJsonAsync<SeedResultResponse>(JsonOptions);
            seedBody!.ProfileImported.Should().BeTrue();
            seedBody.PaAccountsImported.Should().Be(1);

            // Le profil importé est désormais visible via GET /settings sur le tenant cible.
            var overviewResponse = await admin.GetAsync(SettingsPath);
            overviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var settingsBody = await overviewResponse.Content.ReadAsStringAsync();

            // Aucun secret importé : la clé reste vide (placeholder), jamais sérialisée en clair.
            settingsBody.Should().NotContain("PA_API_KEY_FAKE_STAGING", "aucune clé du seed n'est jamais importée (INV-TENANTSETTINGS-007)");

            var overview = JsonSerializer.Deserialize<OverviewResponse>(settingsBody, JsonOptions)!;
            overview.Profile.Should().NotBeNull("le profil vient d'être importé");
            overview.Profile!.Siren.Should().Be("123456782");
            overview.PaAccounts.Should().ContainSingle();
            overview.PaAccounts[0].Account.HasApiKey.Should().BeFalse("la clé API n'est jamais importée — à saisir via la console");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateSeedDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "liakont-admin-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed record SeedResultResponse(bool ProfileImported, bool FiscalImported, int PaAccountsImported);

    private sealed record OverviewResponse(ProfileResponse? Profile, System.Collections.Generic.List<PaAccountResponse> PaAccounts);

    private sealed record ProfileResponse(string Siren, string RaisonSociale);

    private sealed record PaAccountResponse(PaAccountInner Account);

    private sealed record PaAccountInner(string PluginType, bool HasApiKey);
}

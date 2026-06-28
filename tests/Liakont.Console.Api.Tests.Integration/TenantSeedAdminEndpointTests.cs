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
/// inconnu (404), et import nominal (200) qui paramètre le tenant cible (réglages visibles via
/// <c>GET /settings</c>) SANS jamais importer de secret (INV-TENANTSETTINGS-007) ni l'identité légale
/// (jamais seedée — BUG-14). Cible le tenant VIERGE dédié (<see cref="ConsoleApiFactory.TenantSeed"/>)
/// pour ne polluer aucune autre suite.
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class TenantSeedAdminEndpointTests
{
    private const string SettingsPath = "/api/v1/settings";
    private const string SystemAdminRole = "SystemAdmin";

    // BUG-14 : le seed ne porte QUE du paramétrage — l'identité légale n'est jamais seedée.
    private const string ProfileJson = """
        {
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
    public async Task SeedTenant_With_CompanyId_Diverging_From_Registry_Returns_409()
    {
        // Depuis RLM02 (migration V017), tout tenant du registre porte un company_id NON NULL : omettre
        // le company_id (Guid.Empty) retombe désormais sur celui du registre — il n'y a plus de cas
        // « company_id manquant → 400 » pour un tenant enregistré. La garde qui demeure sur le company_id
        // est la DIVERGENCE : un company_id explicite qui ne correspond pas à celui du realm provisionné
        // rendrait le seed invisible aux utilisateurs du tenant → refus (409).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantSeed, ConsoleApiFactory.SystemAdminUserId, roles: SystemAdminRole);

        var response = await client.PostAsJsonAsync(
            SeedPath(ConsoleApiFactory.TenantSeed),
            new { companyId = Guid.NewGuid(), seedDirectoryPath = "/tmp/x" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SeedTenant_With_Missing_SeedDirectoryPath_Returns_400()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantSeed, ConsoleApiFactory.SystemAdminUserId, roles: SystemAdminRole);

        var response = await client.PostAsJsonAsync(
            SeedPath(ConsoleApiFactory.TenantSeed),
            new { companyId = ConsoleApiFactory.TenantSeedCompanyId, seedDirectoryPath = "  " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SeedTenant_As_SystemAdmin_Imports_Settings_Visible_In_Settings_Without_Secret_Or_Identity()
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
            seedBody!.FiscalImported.Should().BeTrue();
            seedBody.PaAccountsImported.Should().Be(1);

            // Aucun secret importé : le compte PA est créé en PLACEHOLDER (clé jamais importée) — le seed le
            // SIGNALE explicitement (INV-TENANTSETTINGS-007 ; jamais de clé en clair, à saisir via la console).
            seedBody.Warnings.Should().Contain(w => w.Contains("non importée", StringComparison.Ordinal));

            // BUG-14 : l'identité légale n'est JAMAIS seedée → aucun profil créé par l'import. Or le récap
            // /settings s'ancre sur le profil (CFG02) : tant qu'il n'est pas saisi (console « Profil légal »),
            // la vue reste VIDE (transitoire, 200). Le paramétrage importé devient visible une fois l'identité
            // saisie — c'est le bandeau « PARAMÉTRAGE INCOMPLET » qui guide l'opérateur dans cet intervalle.
            var overviewResponse = await admin.GetAsync(SettingsPath);
            overviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var overview = JsonSerializer.Deserialize<OverviewResponse>(
                await overviewResponse.Content.ReadAsStringAsync(), JsonOptions)!;
            overview.Profile.Should().BeNull("l'identité légale n'est jamais seedée (BUG-14)");
            overview.PaAccounts.Should().BeEmpty("sans profil, le récap reste vide jusqu'à la saisie de l'identité (CFG02)");

            // Provisioning create-only : un ré-import sur un tenant déjà paramétré (réglages fiscaux présents)
            // est REFUSÉ (409) — il n'écrase JAMAIS des réglages saisis via la console avec la baseline du seed.
            var reseed = await admin.PostAsJsonAsync(
                SeedPath(ConsoleApiFactory.TenantSeed),
                new { companyId = ConsoleApiFactory.TenantSeedCompanyId, seedDirectoryPath = dir });
            reseed.StatusCode.Should().Be(HttpStatusCode.Conflict);
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

    private sealed record SeedResultResponse(
        bool FiscalImported,
        int PaAccountsImported,
        System.Collections.Generic.IReadOnlyList<string> Warnings);

    private sealed record OverviewResponse(ProfileResponse? Profile, System.Collections.Generic.List<PaAccountResponse> PaAccounts);

    private sealed record ProfileResponse(string Siren, string RaisonSociale);

    private sealed record PaAccountResponse(PaAccountInner Account);

    private sealed record PaAccountInner(string PluginType, bool HasApiKey);
}

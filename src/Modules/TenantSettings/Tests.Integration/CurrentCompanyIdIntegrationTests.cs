namespace Liakont.Modules.TenantSettings.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// <c>GetCurrentCompanyId</c> (PIP01a) résout l'unique société du tenant sans <c>companyId</c>. La base
/// de test est PARTAGÉE par la collection (en production la base est par tenant, un seul profil) : on
/// vérifie donc que la valeur retournée est un <c>company_id</c> RÉEL de <c>tenant_profiles</c>, sans
/// présumer lequel le <c>LIMIT 1</c> retourne — robuste à la pollution de fixture.
/// </summary>
[Collection("TenantSettingsIntegration")]
public sealed class CurrentCompanyIdIntegrationTests
{
    private readonly TenantSettingsDatabaseFixture _fixture;

    public CurrentCompanyIdIntegrationTests(TenantSettingsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetCurrentCompanyId_Resolves_A_Company_That_Exists_In_Profiles()
    {
        var companyId = Guid.NewGuid();
        var harness = new TenantSettingsHarness(_fixture, companyId, Guid.NewGuid());
        await InsertProfileAsync(harness, companyId);

        var resolved = await harness.Queries.GetCurrentCompanyId();

        resolved.Should().NotBeNull("au moins un profil tenant existe désormais dans la base");
        (await ProfileExistsAsync(harness, resolved!.Value)).Should().BeTrue(
            "la valeur retournée doit être un company_id réel de tenant_profiles, jamais une valeur inventée");
    }

    private static async Task InsertProfileAsync(TenantSettingsHarness harness, Guid companyId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO tenantsettings.tenant_profiles
                (company_id, siren, raison_sociale, address_street, address_postal_code, address_city, address_country)
            VALUES (@CompanyId, '111111111', 'Société Fictive', '1 rue de Test', '35000', 'Rennes', 'FR')
            """,
            new { CompanyId = companyId });
    }

    private static async Task<bool> ProfileExistsAsync(TenantSettingsHarness harness, Guid companyId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        var count = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM tenantsettings.tenant_profiles WHERE company_id = @CompanyId",
            new { CompanyId = companyId });
        return count > 0;
    }
}

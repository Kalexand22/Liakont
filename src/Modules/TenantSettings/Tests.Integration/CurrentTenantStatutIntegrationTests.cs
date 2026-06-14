namespace Liakont.Modules.TenantSettings.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// <c>GetCurrentTenantStatut</c> (OPS03.4 lot B) résout le statut métier du tenant courant sans
/// <c>companyId</c> (même patron que <c>GetCurrentCompanyId</c>). La base de test est PARTAGÉE par
/// la collection : la lecture <c>LIMIT 1</c> ne présume pas QUELLE ligne sort — on pilote donc le
/// statut de TOUTES les lignes (en production la base est par tenant : une seule ligne), puis on
/// RESTAURE « Actif » pour ne polluer aucune autre suite.
/// </summary>
[Collection("TenantSettingsIntegration")]
public sealed class CurrentTenantStatutIntegrationTests : IAsyncLifetime
{
    private readonly TenantSettingsDatabaseFixture _fixture;

    public CurrentTenantStatutIntegrationTests(TenantSettingsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Restaure « Actif » sur toutes les lignes : aucune pollution des suites suivantes.
        var harness = NewHarness();
        using var conn = await harness.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync("UPDATE tenantsettings.tenant_profiles SET statut = 0");
    }

    [Fact]
    public async Task GetCurrentTenantStatut_Reflects_The_Profile_Status()
    {
        var harness = NewHarness();
        var companyId = Guid.NewGuid();
        await InsertProfileAsync(harness, companyId);

        // Toutes les lignes suspendues (base partagée — LIMIT 1 sans ordre garanti).
        await SetAllStatutAsync(harness, 1);
        (await harness.Queries.GetCurrentTenantStatut()).Should().Be("Suspendu");

        await SetAllStatutAsync(harness, 0);
        (await harness.Queries.GetCurrentTenantStatut()).Should().Be("Actif");
    }

    private static async Task SetAllStatutAsync(TenantSettingsHarness harness, int statut)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE tenantsettings.tenant_profiles SET statut = @Statut", new { Statut = statut });
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

    private TenantSettingsHarness NewHarness() => new(_fixture, Guid.NewGuid(), Guid.NewGuid());
}

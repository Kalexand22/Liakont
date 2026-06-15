namespace Liakont.Host.Tests.Unit.Startup;

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Liakont.Host.Startup;
using Microsoft.Extensions.Configuration;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Vérifie la DONNÉE de seed du 2e tenant (RLM01 D3) telle qu'elle est committée dans
/// <c>appsettings.Development.json</c> : un tenant additionnel au <c>company_id</c> DISTINCT du
/// <c>default</c>, avec les champs NOT NULL/UNIQUE requis pour ne pas être ignoré par le seeder.
/// <para>
/// Anti-faux-vert du côté « registre <c>outbox.tenants</c> » de l'isolation : la donnée doit être
/// correcte et distincte (l'INSERT lui-même est couvert par la compilation/analyse). La preuve de
/// résolution effective (company_id → tenant) relève de RLM02 ; la preuve E2E, de la GATE.
/// </para>
/// </summary>
public sealed class DevAdditionalTenantsConfigTests
{
    private const string DefaultCompanyId = "00000000-0000-4000-a000-000000000001";
    private const string SecondTenantCompanyId = "00000000-0000-4000-a000-000000000002";

    [Fact]
    public void Development_Config_Seeds_A_Second_Tenant_With_A_Distinct_CompanyId()
    {
        var options = BindDevTenantSeed();

        options.CompanyId.Should().Be(Guid.Parse(DefaultCompanyId), "le tenant default garde son company_id de dev");

        var second = options.AdditionalTenants.Should().ContainSingle().Subject;
        second.TenantId.Should().NotBeNullOrWhiteSpace();
        second.RealmName.Should().NotBeNullOrWhiteSpace("realm_name est NOT NULL/UNIQUE — sinon le seed est ignoré");
        second.DatabaseName.Should().NotBeNullOrWhiteSpace("database_name est NOT NULL — sinon le seed est ignoré");
        second.CompanyId.Should().Be(Guid.Parse(SecondTenantCompanyId));
        second.CompanyId.Should().NotBe(options.CompanyId, "l'isolation exige deux company_id DIFFÉRENTS");
    }

    [Fact]
    public void Second_Tenant_DatabaseName_Matches_The_Runtime_Derived_Name()
    {
        var options = BindDevTenantSeed();
        var second = options.AdditionalTenants.Should().ContainSingle().Subject;

        // Sans override TenantConnections (cas de tenant2 en dev), le runtime DÉRIVE le nom de base
        // {DatabasePrefix}{tenantId avec '-'→'_'} (TenantAwareNpgsqlConnectionFactory), tandis que la
        // migration au démarrage lit database_name du registre. Les deux DOIVENT coïncider, sinon la base
        // dérivée n'existe jamais → 3D000 sur toute requête tenant (finding F3/RLF01).
        var derived = new TenantConnectionOptions().DatabasePrefix + second.TenantId.Replace('-', '_');
        second.DatabaseName.Should().Be(
            derived,
            "database_name du registre doit coïncider avec le nom dérivé au runtime (cohérence RLF01)");
    }

    [Theory]
    [InlineData("stratum_tenant2", true)]
    [InlineData("liakont", true)]
    [InlineData("stratum_a_b_2", true)]
    [InlineData("stratum_tenant-2", false)] // tiret interdit (l'id dérivé remplace déjà '-' par '_')
    [InlineData("Stratum_Tenant2", false)] // majuscules
    [InlineData("2tenant", false)] // ne commence pas par une lettre
    [InlineData("\"; DROP DATABASE liakont; --", false)] // tentative d'injection
    [InlineData("", false)]
    public void IsSafeDatabaseIdentifier_Accepts_Only_Strictly_Safe_Names(string name, bool expected)
    {
        DevTenantSeeder.IsSafeDatabaseIdentifier(name).Should().Be(expected);
    }

    private static DevTenantSeedOptions BindDevTenantSeed()
    {
        var path = Path.Combine(FindHostProjectDir(), "appsettings.Development.json");
        File.Exists(path).Should().BeTrue($"appsettings.Development.json introuvable : {path}");

        var config = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
        var options = config.GetSection("DevTenantSeed").Get<DevTenantSeedOptions>();
        options.Should().NotBeNull();
        return options!;
    }

    private static string FindHostProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Host", "Liakont.Host");
            if (File.Exists(Path.Combine(candidate, "appsettings.Development.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Dossier du projet Host introuvable depuis " + AppContext.BaseDirectory);
    }
}

namespace Liakont.Host.Tests.Unit.Startup;

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Liakont.Host.Startup;
using Microsoft.Extensions.Configuration;
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

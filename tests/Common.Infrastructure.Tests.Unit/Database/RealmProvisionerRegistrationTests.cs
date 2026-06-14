namespace Stratum.Common.Infrastructure.Tests.Unit.Database;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Keycloak;
using Xunit;

/// <summary>
/// RLM04 (ADR-0021 §1) — preuve du SEAM par la résolution DI : le profil SaaS PARTAGÉ (défaut)
/// résout la <see cref="NoOpKeycloakRealmProvisioner"/> (aucun realm/client par tenant), tandis que
/// le profil DÉDIÉ mono-tenant (<c>Keycloak:DedicatedRealmPerTenant=true</c>) garde la vraie
/// <see cref="KeycloakRealmProvisioner"/>. C'est la garde structurelle « aucun POST /admin/realms en
/// partagé » : ce n'est pas « 0 HTTP contre un fake muet », c'est le no-op (sans dépendance HTTP) qui
/// est réellement câblé.
/// </summary>
public sealed class RealmProvisionerRegistrationTests
{
    private static ServiceProvider BuildProvider(bool dedicated)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Host=localhost;Database=liakont;Username=u;Password=p",
            ["TenantConnections:DatabasePrefix"] = "liakont_",
            ["Keycloak:AdminBaseUrl"] = "http://localhost:8080",
            ["Keycloak:AdminUsername"] = "admin",
            ["Keycloak:AdminPassword"] = "admin",
            ["Keycloak:DedicatedRealmPerTenant"] = dedicated ? "true" : "false",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStratumDatabase(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void SharedProfile_Default_Resolves_NoOpRealmProvisioner()
    {
        using var provider = BuildProvider(dedicated: false);

        var resolved = provider.GetRequiredService<IKeycloakRealmProvisioner>();

        Assert.IsType<NoOpKeycloakRealmProvisioner>(resolved);
    }

    [Fact]
    public void DedicatedProfile_Resolves_RealKeycloakRealmProvisioner()
    {
        using var provider = BuildProvider(dedicated: true);

        var resolved = provider.GetRequiredService<IKeycloakRealmProvisioner>();

        Assert.IsType<KeycloakRealmProvisioner>(resolved);
    }
}

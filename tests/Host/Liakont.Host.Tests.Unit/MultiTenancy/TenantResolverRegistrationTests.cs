namespace Liakont.Host.Tests.Unit.MultiTenancy;

using System.Linq;
using FluentAssertions;
using Liakont.Host.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Garde structurelle de l'ordre de la chaîne de résolution (ADR-0021 §2c) : le résolveur du claim
/// <c>company_id</c> est enregistré EN PREMIER (= prioritaire dans <see cref="CompositeTenantResolver"/>),
/// donc la voie jeton est AUTORITAIRE devant les voies client-fournies (sous-domaine, header).
/// </summary>
public sealed class TenantResolverRegistrationTests
{
    [Fact]
    public void CompanyClaimResolver_Is_Registered_First_So_Token_Wins()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddStratumMultiTenancy(configuration);

        var resolverImplementations = services
            .Where(d => d.ServiceType == typeof(ITenantResolver))
            .Select(d => d.ImplementationType)
            .ToList();

        resolverImplementations.Should().NotBeEmpty();
        resolverImplementations[0].Should().Be<CompanyClaimTenantResolver>(
            "la voie jeton company_id est autoritaire : enregistrée avant sous-domaine/header (ADR-0021 §2c)");

        // Les voies client-fournies restent dans la chaîne (fallback hors authentification de tenant),
        // mais APRÈS le résolveur autoritaire.
        resolverImplementations.Should().Contain(typeof(SubdomainTenantResolver));
        resolverImplementations.Should().Contain(typeof(HeaderTenantResolver));
        resolverImplementations.IndexOf(typeof(CompanyClaimTenantResolver))
            .Should().BeLessThan(resolverImplementations.IndexOf(typeof(SubdomainTenantResolver)));
    }

    [Fact]
    public void CompanyTenantLookup_Is_Registered()
    {
        var services = new ServiceCollection();
        services.AddStratumMultiTenancy(new ConfigurationBuilder().Build());

        services.Should().Contain(d => d.ServiceType == typeof(ICompanyTenantLookup));
    }
}

namespace Liakont.Host.Tests.Unit.MultiTenancy;

using System.Linq;
using FluentAssertions;
using Liakont.Host.MultiTenancy;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Garde d'architecture (INV-0021-9) : la résolution de tenant (et, par extension, la lecture du
/// <c>company_id</c> qu'elle exploite) ne dépend d'AUCUN type Keycloak. Le claim <c>company_id</c> est
/// lu via les abstractions standard (<c>ClaimsPrincipal</c> / <c>IActorContext</c>), jamais via un type
/// IdP-spécifique — la couche d'auth reste derrière l'abstraction IdP (CLAUDE.md n°6).
/// </summary>
public sealed class TenantResolutionArchitectureTests
{
    private static readonly string[] KeycloakNamespaces =
    [
        "Liakont.Host.Security.Keycloak",
        "Stratum.Common.Infrastructure.Keycloak",
    ];

    [Fact]
    public void TenantResolution_Does_Not_Depend_On_Any_Keycloak_Type()
    {
        var result = Types.InAssembly(typeof(CompanyClaimTenantResolver).Assembly)
            .That()
            .ResideInNamespace("Liakont.Host.MultiTenancy")
            .Should()
            .NotHaveDependencyOnAny(KeycloakNamespaces)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "INV-0021-9 : la résolution de tenant lit company_id via les abstractions, jamais via un type "
            + "Keycloak — types fautifs : {0}",
            result.FailingTypeNames is null ? "(aucun)" : string.Join(", ", result.FailingTypeNames));
    }
}

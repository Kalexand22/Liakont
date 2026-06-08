namespace Liakont.Host.Tests.Unit.Security;

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Security;
using Microsoft.AspNetCore.Authorization;
using Xunit;

/// <summary>
/// Vérifie que la garde endpoint (<see cref="PermissionAuthorizationHandler"/>) autorise sur le claim
/// <c>permission</c> du principal — le MÊME mécanisme que l'UI (ADR-0017, INV-IDN01-3) — pour les 4
/// rôles de test §4, refuse en l'absence du claim, et conserve le court-circuit super-admin.
/// </summary>
public sealed class PermissionAuthorizationHandlerTests
{
    [Theory]
    [InlineData(LiakontPermissions.Read, LiakontPermissions.Read, true)]
    [InlineData(LiakontPermissions.Read, LiakontPermissions.Actions, false)]
    [InlineData(LiakontPermissions.Actions, LiakontPermissions.Actions, true)]
    [InlineData(LiakontPermissions.Settings, LiakontPermissions.Supervision, false)]
    public async Task Handler_Should_Decide_From_Permission_Claim(
        string heldPermission,
        string requiredPermission,
        bool expectedSucceeded)
    {
        var user = PrincipalWith(new Claim(RolePermissionCatalog.PermissionClaimType, heldPermission));

        var context = await EvaluateAsync(user, requiredPermission);

        context.HasSucceeded.Should().Be(expectedSucceeded);
    }

    [Fact]
    public async Task Handler_Should_Deny_When_No_Permission_Claim()
    {
        var user = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"));

        var context = await EvaluateAsync(user, LiakontPermissions.Read);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handler_Should_Match_Permission_Case_Insensitively()
    {
        var user = PrincipalWith(new Claim(RolePermissionCatalog.PermissionClaimType, "LIAKONT.READ"));

        var context = await EvaluateAsync(user, LiakontPermissions.Read);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_Should_Bypass_For_SuperAdmin_Without_Permission_Claim()
    {
        // Aucun claim "permission", mais un rôle super-admin realm (Keycloak "stratum-admin").
        var user = PrincipalWith(new Claim("roles", "stratum-admin"));

        var context = await EvaluateAsync(user, LiakontPermissions.Supervision);

        context.HasSucceeded.Should().BeTrue();
    }

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));

    private static async Task<AuthorizationHandlerContext> EvaluateAsync(
        ClaimsPrincipal user,
        string requiredPermission)
    {
        var requirement = new PermissionRequirement(requiredPermission);
        var context = new AuthorizationHandlerContext(
            new List<IAuthorizationRequirement> { requirement },
            user,
            resource: null);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        return context;
    }
}

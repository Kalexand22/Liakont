namespace Liakont.Host.Tests.Unit.Security;

using System;
using System.Security.Claims;
using FluentAssertions;
using Liakont.Host.Security;
using Xunit;

/// <summary>
/// Vérifie la décision PURE de bornage de la fenêtre de révocation des permissions sensibles
/// (<see cref="SensitivePermissionRevocation"/>, RDF10 / ADR-0017 §Négatif) : une session non
/// super-admin porteuse de <c>liakont.actions</c>/<c>liakont.settings</c> est plafonnée à une fenêtre
/// absolue courte ; les sessions super-admin, ou sans permission sensible, gardent le défaut glissant.
/// </summary>
public sealed class SensitivePermissionRevocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    [Theory]
    [InlineData(LiakontPermissions.Actions)]
    [InlineData(LiakontPermissions.Settings)]
    public void Caps_Session_For_NonSuperAdmin_Holding_A_Sensitive_Permission(string permission)
    {
        var user = PrincipalWith(new Claim(RolePermissionCatalog.PermissionClaimType, permission));

        var decision = SensitivePermissionRevocation.Resolve(user, Now, Window);

        decision.Cap.Should().BeTrue();
        decision.ExpiresUtc.Should().Be(Now + Window);
    }

    [Fact]
    public void Caps_Session_Case_Insensitively()
    {
        var user = PrincipalWith(new Claim(RolePermissionCatalog.PermissionClaimType, "LIAKONT.SETTINGS"));

        SensitivePermissionRevocation.Resolve(user, Now, Window).Cap.Should().BeTrue();
    }

    [Fact]
    public void Does_Not_Cap_When_Only_NonSensitive_Permissions_Are_Held()
    {
        // « lecture » ne porte que liakont.read (matrice §3) — fenêtre glissante par défaut conservée.
        var user = PrincipalWith(new Claim(RolePermissionCatalog.PermissionClaimType, LiakontPermissions.Read));

        var decision = SensitivePermissionRevocation.Resolve(user, Now, Window);

        decision.Cap.Should().BeFalse();
        decision.ExpiresUtc.Should().BeNull();
    }

    [Fact]
    public void Does_Not_Cap_When_No_Permission_Claim()
    {
        var user = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"));

        SensitivePermissionRevocation.Resolve(user, Now, Window).Cap.Should().BeFalse();
    }

    [Fact]
    public void Does_Not_Cap_SuperAdmin_Even_With_Sensitive_Permission()
    {
        // Court-circuit super-admin préservé : un stratum-admin n'est jamais plafonné.
        var user = PrincipalWith(
            new Claim("roles", "stratum-admin"),
            new Claim(RolePermissionCatalog.PermissionClaimType, LiakontPermissions.Actions));

        SensitivePermissionRevocation.Resolve(user, Now, Window).Cap.Should().BeFalse();
    }

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));
}

namespace Liakont.Host.Tests.Unit.Security;

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using FluentAssertions;
using Liakont.Host.Security;
using Xunit;

/// <summary>
/// Vérifie que <see cref="RolePermissionCatalog"/> matérialise EXACTEMENT la matrice §3 de
/// <c>docs/architecture/identity-permissions-liakont.md</c> (ADR-0017) : 4 rôles × 4 permissions,
/// union pour les rôles cumulés, rôle inconnu sans permission, insensibilité à la casse, et
/// projection idempotente des claims <c>permission</c> depuis les rôles realm.
/// </summary>
public sealed class RolePermissionCatalogTests
{
    public static IEnumerable<object[]> RoleMatrix() =>
    [
        ["lecture", new[] { LiakontPermissions.Read }],
        ["operateur", new[] { LiakontPermissions.Read, LiakontPermissions.Actions }],
        ["parametrage", new[] { LiakontPermissions.Read, LiakontPermissions.Actions, LiakontPermissions.Settings }],
        [
            "superviseur",
            new[]
            {
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.Settings,
                LiakontPermissions.Supervision,
            },
        ],
    ];

    [Theory]
    [MemberData(nameof(RoleMatrix))]
    public void PermissionsForRoles_Should_Match_Section3_Matrix(string role, string[] expected)
    {
        var permissions = RolePermissionCatalog.PermissionsForRoles([role]);

        permissions.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void PermissionsForRoles_Should_Union_Cumulative_Roles()
    {
        // Le superviseur §4 porte les quatre rôles realm cumulés → union = les quatre permissions.
        var permissions = RolePermissionCatalog.PermissionsForRoles(
            ["lecture", "operateur", "parametrage", "superviseur"]);

        permissions.Should().BeEquivalentTo(
            [
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.Settings,
                LiakontPermissions.Supervision,
            ]);
    }

    [Fact]
    public void PermissionsForRoles_Should_Be_Case_Insensitive_On_Role_Name()
    {
        var permissions = RolePermissionCatalog.PermissionsForRoles(["OPERATEUR"]);

        permissions.Should().BeEquivalentTo([LiakontPermissions.Read, LiakontPermissions.Actions]);
    }

    [Fact]
    public void PermissionsForRoles_Should_Grant_Nothing_For_Unknown_Role()
    {
        var permissions = RolePermissionCatalog.PermissionsForRoles(["role-inexistant", "admin"]);

        permissions.Should().BeEmpty();
    }

    [Fact]
    public void ProjectPermissionClaims_Should_Add_Permission_Claims_From_Realm_Roles()
    {
        var identity = new ClaimsIdentity([new Claim("roles", "operateur")], "Test");

        RolePermissionCatalog.ProjectPermissionClaims(identity);

        PermissionValues(identity).Should().BeEquivalentTo([LiakontPermissions.Read, LiakontPermissions.Actions]);
    }

    [Fact]
    public void ProjectPermissionClaims_Should_Be_Idempotent()
    {
        var identity = new ClaimsIdentity(
            [new Claim("roles", "lecture"), new Claim("roles", "operateur")],
            "Test");

        RolePermissionCatalog.ProjectPermissionClaims(identity);
        RolePermissionCatalog.ProjectPermissionClaims(identity);

        // Aucun doublon malgré la double projection.
        PermissionValues(identity).Should().BeEquivalentTo([LiakontPermissions.Read, LiakontPermissions.Actions]);
    }

    [Fact]
    public void PermissionsForPrincipal_Should_Read_Roles_From_Principal()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("roles", "lecture"), new Claim("roles", "parametrage")],
            "Test"));

        var permissions = RolePermissionCatalog.PermissionsForPrincipal(principal);

        permissions.Should().BeEquivalentTo(
            [LiakontPermissions.Read, LiakontPermissions.Actions, LiakontPermissions.Settings]);
    }

    [Fact]
    public void Exploitant_Role_Grants_Only_Fleet_Permission()
    {
        // Rôle IT Innovations (OPS04) : la permission liakont.fleet est RÉELLEMENT accordée par un rôle
        // (sinon le dashboard /flotte ne serait ouvert qu'aux super-admins, contredisant la doc §2/§3).
        var permissions = RolePermissionCatalog.PermissionsForRoles(["exploitant"]);

        permissions.Should().BeEquivalentTo([LiakontPermissions.Fleet]);
    }

    [Fact]
    public void Editor_Roles_Do_Not_Grant_Fleet_Permission()
    {
        // Cloisonnement : un rôle éditeur (même cumulé jusqu'au superviseur) ne reçoit JAMAIS liakont.fleet.
        var permissions = RolePermissionCatalog.PermissionsForRoles(
            ["lecture", "operateur", "parametrage", "superviseur"]);

        permissions.Should().NotContain(LiakontPermissions.Fleet);
    }

    private static List<string> PermissionValues(ClaimsIdentity identity) =>
        identity.FindAll(RolePermissionCatalog.PermissionClaimType).Select(c => c.Value).ToList();
}

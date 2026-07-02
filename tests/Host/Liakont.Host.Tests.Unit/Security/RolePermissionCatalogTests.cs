namespace Liakont.Host.Tests.Unit.Security;

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using FluentAssertions;
using Liakont.Host.Security;
using Xunit;

/// <summary>
/// Vérifie que <see cref="RolePermissionCatalog"/> matérialise EXACTEMENT la matrice §3 de
/// <c>docs/architecture/identity-permissions-liakont.md</c> (ADR-0017), colonnes GED comprises
/// (amendement GED06 : <c>ged.read</c> / <c>ged.export</c> / <c>ged.confidential</c>) : union pour
/// les rôles cumulés, rôle inconnu sans permission, insensibilité à la casse, et projection
/// idempotente des claims <c>permission</c> depuis les rôles realm.
/// </summary>
public sealed class RolePermissionCatalogTests
{
    public static IEnumerable<object[]> RoleMatrix() =>
    [
        ["lecture", new[] { LiakontPermissions.Read, LiakontPermissions.GedRead }],
        [
            "operateur",
            new[]
            {
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
            },
        ],
        [
            "parametrage",
            new[]
            {
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.Settings,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
            },
        ],
        [
            "superviseur",
            new[]
            {
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.Settings,
                LiakontPermissions.Supervision,
                LiakontPermissions.InstanceSettings,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
                LiakontPermissions.GedConfidential,
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
        // Le superviseur §4 porte les quatre rôles realm cumulés → union = toutes les permissions
        // éditeur, GED comprises (ged.read/export/confidential accordés au superviseur).
        var permissions = RolePermissionCatalog.PermissionsForRoles(
            ["lecture", "operateur", "parametrage", "superviseur"]);

        permissions.Should().BeEquivalentTo(
            [
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.Settings,
                LiakontPermissions.Supervision,
                LiakontPermissions.InstanceSettings,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
                LiakontPermissions.GedConfidential,
            ]);
    }

    [Fact]
    public void PermissionsForRoles_Should_Be_Case_Insensitive_On_Role_Name()
    {
        var permissions = RolePermissionCatalog.PermissionsForRoles(["OPERATEUR"]);

        permissions.Should().BeEquivalentTo(
            [
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
            ]);
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

        PermissionValues(identity).Should().BeEquivalentTo(
            [
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
            ]);
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
        PermissionValues(identity).Should().BeEquivalentTo(
            [
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
            ]);
    }

    [Fact]
    public void PermissionsForPrincipal_Should_Read_Roles_From_Principal()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("roles", "lecture"), new Claim("roles", "parametrage")],
            "Test"));

        var permissions = RolePermissionCatalog.PermissionsForPrincipal(principal);

        permissions.Should().BeEquivalentTo(
            [
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.Settings,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
            ]);
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

    // ── GED06 (F19 §6.5, ADR-0032/0035/0036) : amendement des colonnes GED de la matrice §3 ──
    [Fact]
    public void ProjectPermissionClaims_Should_Project_All_Three_Ged_Permissions()
    {
        // Acceptance GED06 #1 : les 3 permissions GED sont projetées PAR RolePermissionCatalog au sign-in.
        // La projection est le MÊME point unique (ProjectPermissionClaims) appelé par les deux flux —
        // OIDC/cookie (KeycloakIdentityProviderAuthenticator OnTokenValidated) ET JwtBearer : prouver la
        // projection ici couvre les deux schémas (INV-IDN01-2/3). Le superviseur porte les 3 permissions GED.
        var identity = new ClaimsIdentity([new Claim("roles", "superviseur")], "Test");

        RolePermissionCatalog.ProjectPermissionClaims(identity);

        PermissionValues(identity).Should().Contain(
            [
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
                LiakontPermissions.GedConfidential,
            ]);
    }

    [Fact]
    public void Ged_Confidential_Is_Distinct_From_Ged_Read()
    {
        // Acceptance GED06 #2 : ged.confidential est DISTINCTE de ged.read — il existe des rôles qui
        // consultent la GED (ged.read) SANS accéder aux axes/entités confidentiels (ged.confidential).
        // (Le masquage server-side qui consomme ce droit est porté par GED08/GED09 ; ici on prouve la
        // distinction au niveau du catalogue de permissions.)
        foreach (var role in new[] { "lecture", "operateur", "parametrage" })
        {
            var permissions = RolePermissionCatalog.PermissionsForRoles([role]);

            permissions.Should().Contain(LiakontPermissions.GedRead, $"{role} consulte la GED");
            permissions.Should().NotContain(
                LiakontPermissions.GedConfidential,
                $"{role} n'accède pas aux axes/entités confidentiels (seul superviseur)");
        }
    }

    [Fact]
    public void Lecture_Grants_Ged_Read_But_Not_Ged_Export()
    {
        // L'export GED est gardé SÉPARÉMENT de la lecture (ADR-0036 §4) : la consultation pure (`lecture`)
        // n'autorise pas l'export.
        var permissions = RolePermissionCatalog.PermissionsForRoles(["lecture"]);

        permissions.Should().Contain(LiakontPermissions.GedRead);
        permissions.Should().NotContain(LiakontPermissions.GedExport);
    }

    [Fact]
    public void Only_Superviseur_Grants_Ged_Confidential()
    {
        // Moindre privilège : ged.confidential (le plus sensible) n'est accordé qu'au superviseur.
        RolePermissionCatalog.PermissionsForRoles(["superviseur"])
            .Should().Contain(LiakontPermissions.GedConfidential);

        foreach (var role in new[] { "lecture", "operateur", "parametrage", "exploitant" })
        {
            RolePermissionCatalog.PermissionsForRoles([role])
                .Should().NotContain(LiakontPermissions.GedConfidential, $"{role} < superviseur");
        }
    }

    [Fact]
    public void Exploitant_Role_Grants_No_Ged_Permission()
    {
        // Le rôle IT Innovations (flotte) n'est PAS un rôle éditeur : aucune permission GED (comme aucune
        // permission éditeur) — parité avec le cloisonnement fleet ↔ éditeur.
        var permissions = RolePermissionCatalog.PermissionsForRoles(["exploitant"]);

        permissions.Should().NotContain(
            [
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
                LiakontPermissions.GedConfidential,
            ]);
    }

    [Fact]
    public void Ged_Permissions_Are_Liakont_Dedicated_Never_Socle()
    {
        // FIX07c / RL-35 : jamais une permission socle accordée à un rôle Liakont. Les 3 permissions GED
        // sont des permissions Liakont DÉDIÉES (préfixe "liakont.ged.") — jamais un préfixe socle (audit.,
        // job., identity., notification.…).
        var gedPermissions = new[]
        {
            LiakontPermissions.GedRead,
            LiakontPermissions.GedExport,
            LiakontPermissions.GedConfidential,
        };

        gedPermissions.Should().OnlyContain(p => p.StartsWith("liakont.ged.", System.StringComparison.Ordinal));

        // Toute permission projetée par un rôle éditeur est une permission Liakont ("liakont.") — jamais socle.
        var editorPermissions = RolePermissionCatalog.PermissionsForRoles(
            ["lecture", "operateur", "parametrage", "superviseur"]);

        editorPermissions.Should().OnlyContain(p => p.StartsWith("liakont.", System.StringComparison.Ordinal));
    }

    private static List<string> PermissionValues(ClaimsIdentity identity) =>
        identity.FindAll(RolePermissionCatalog.PermissionClaimType).Select(c => c.Value).ToList();
}

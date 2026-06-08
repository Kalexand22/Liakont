namespace Liakont.Host.Tests.Unit.Security;

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Navigation;
using Liakont.Host.Security;
using Microsoft.AspNetCore.Components.Authorization;
using Xunit;

/// <summary>
/// Preuve de bout en bout (hors navigateur) de la chaîne d'autorisation produit sur une surface
/// permission-gated DÉJÀ existante (la nav Supervision de WEB01) : rôles realm → projection §3
/// (<see cref="RolePermissionCatalog"/>) → claims <c>permission</c> → garde UI réelle
/// (<see cref="ClaimsPermissionService"/>) → visibilité de la nav (<see cref="LiakontNavSectionProvider"/>).
/// Pour les 4 utilisateurs de test §4, un rôle élevé NON super-admin voit EXACTEMENT les éléments que
/// ses rôles accordent (ADR-0017, INV-IDN01-4) — anti-faux-vert : l'élément est réellement visible pour
/// le rôle qui le porte, et réellement masqué sinon.
/// </summary>
public sealed class RolePermissionNavVisibilityTests
{
    public static IEnumerable<object[]> Section4Users() =>
    [
        ["lecture", new[] { "lecture" }, false],
        ["operateur", new[] { "lecture", "operateur" }, false],
        ["parametrage", new[] { "lecture", "operateur", "parametrage" }, false],
        ["superviseur", new[] { "lecture", "operateur", "parametrage", "superviseur" }, true],
    ];

    [Theory]
    [MemberData(nameof(Section4Users))]
    public void Supervision_Nav_Visible_Exactly_When_Role_Grants_Supervision(
        string label,
        string[] realmRoles,
        bool expectSupervisionVisible)
    {
        _ = label;
        using var permissionService = BuildPermissionService(realmRoles);

        var section = new LiakontNavSectionProvider(permissionService, new FakeConsoleContext())
            .GetSection();

        var labels = section.Items.Select(item => item.Label).ToList();

        // Les éléments toujours présents (non gardés) restent visibles pour tout rôle.
        labels.Should().Contain(["Documents", "Encaissements", "Traitements", "Paramétrage"]);

        // Supervision : visible UNIQUEMENT pour le rôle superviseur (liakont.supervision).
        if (expectSupervisionVisible)
        {
            labels.Should().Contain("Supervision");
        }
        else
        {
            labels.Should().NotContain("Supervision");
        }
    }

    [Theory]
    [MemberData(nameof(Section4Users))]
    public void Claims_Permission_Service_Reflects_Section3_For_Each_Role(
        string label,
        string[] realmRoles,
        bool expectSupervisionVisible)
    {
        _ = label;
        _ = expectSupervisionVisible;
        using var permissionService = BuildPermissionService(realmRoles);

        var expected = RolePermissionCatalog.PermissionsForRoles(realmRoles);

        permissionService.HasPermission(LiakontPermissions.Read)
            .Should().Be(expected.Contains(LiakontPermissions.Read));
        permissionService.HasPermission(LiakontPermissions.Actions)
            .Should().Be(expected.Contains(LiakontPermissions.Actions));
        permissionService.HasPermission(LiakontPermissions.Settings)
            .Should().Be(expected.Contains(LiakontPermissions.Settings));
        permissionService.HasPermission(LiakontPermissions.Supervision)
            .Should().Be(expected.Contains(LiakontPermissions.Supervision));
    }

    private static ClaimsPermissionService BuildPermissionService(string[] realmRoles)
    {
        var identity = new ClaimsIdentity(
            realmRoles.Select(role => new Claim("roles", role)),
            authenticationType: "Test");

        // Reproduit la projection au sign-in OIDC (production) : rôles realm → claims "permission".
        RolePermissionCatalog.ProjectPermissionClaims(identity);

        var principal = new ClaimsPrincipal(identity);
        return new ClaimsPermissionService(new FakeAuthenticationStateProvider(principal));
    }

    private sealed class FakeAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly AuthenticationState _state;

        public FakeAuthenticationStateProvider(ClaimsPrincipal principal) =>
            _state = new AuthenticationState(principal);

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
    }

    private sealed class FakeConsoleContext : ILiakontConsoleContext
    {
        public bool ReconciliationAvailable => false;

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

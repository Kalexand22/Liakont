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
/// (<see cref="ClaimsPermissionService"/>) → visibilité de la nav (<see cref="LiakontNavNodeProvider"/>).
/// Pour les 4 utilisateurs de test §4, un rôle élevé NON super-admin voit EXACTEMENT les éléments que
/// ses rôles accordent (ADR-0017, INV-IDN01-4) — anti-faux-vert : l'élément est réellement visible pour
/// le rôle qui le porte, et réellement masqué sinon.
/// </summary>
public sealed class RolePermissionNavVisibilityTests
{
    // Tuple : label, realmRoles, expectActions (Traitements), expectSettings (Paramétrage), expectSupervision.
    public static IEnumerable<object[]> Section4Users() =>
    [
        ["lecture", new[] { "lecture" }, false, false, false],
        ["operateur", new[] { "lecture", "operateur" }, true, false, false],
        ["parametrage", new[] { "lecture", "operateur", "parametrage" }, true, true, false],
        ["superviseur", new[] { "lecture", "operateur", "parametrage", "superviseur" }, true, true, true],
    ];

    [Theory]
    [MemberData(nameof(Section4Users))]
    public void Operator_Nav_Visible_Exactly_When_Role_Grants_The_Permission(
        string label,
        string[] realmRoles,
        bool expectActionsVisible,
        bool expectSettingsVisible,
        bool expectSupervisionVisible)
    {
        _ = label;
        using var permissionService = BuildPermissionService(realmRoles);

        var root = new LiakontNavNodeProvider(permissionService, new FakeConsoleContext())
            .GetNavNode();

        var labels = root.Children.Select(item => item.Label).ToList();

        // Documents / Encaissements (consultation, liakont.read) : visibles pour tout rôle §4 (tous portent lecture).
        labels.Should().Contain(["Documents", "Encaissements"]);

        // Traitements (liakont.actions) : caché pour un simple lecteur (finding F5a / RLF03).
        if (expectActionsVisible)
        {
            labels.Should().Contain("Traitements");
        }
        else
        {
            labels.Should().NotContain("Traitements");
        }

        // Paramétrage : le HUB est visible à tout porteur de liakont.read (les 4 rôles §4 portent lecture) —
        // il sert l'export d'audit par période (FIX208, capacité liakont.read ; le masquer régresserait cette
        // capacité). Le SOUS-MENU de paramétrage (table TVA, comptes PA, …) n'apparaît qu'au porteur de
        // liakont.settings (parametrage/superviseur). Cf. RLF03 / finding F5a, contraint par FIX208.
        labels.Should().Contain("Paramétrage");
        root.Children.Single(c => c.Label == "Paramétrage").HasChildren
            .Should().Be(expectSettingsVisible, "le sous-menu de paramétrage est réservé au porteur de liakont.settings");

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
        bool expectActionsVisible,
        bool expectSettingsVisible,
        bool expectSupervisionVisible)
    {
        _ = label;
        _ = expectActionsVisible;
        _ = expectSettingsVisible;
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

        public int ReconciliationPendingCount => 0;

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

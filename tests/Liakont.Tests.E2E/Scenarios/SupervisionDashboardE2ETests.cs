namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E du dashboard de supervision (SUP02) : un utilisateur de rôle <c>superviseur</c> (porteur de
/// <c>liakont.supervision</c>) se connecte (flux Keycloak OIDC), ouvre la Supervision depuis la navigation
/// Liakont et voit le dashboard se rendre RÉELLEMENT contre le backend — l'agrégation cross-tenant
/// (<c>CrossTenantSupervisionDashboardQueries</c> : énumération des tenants via le registre système puis
/// lectures tenant-scopées) s'exécute sans bandeau d'erreur. Prouve le « parcours supervision » côté
/// atteinte + rendu de la vue d'ensemble, là où les tests bUnit (SupervisionTests / SupervisionDetailTests)
/// couvrent l'interaction détaillée (détail d'un tenant, ACQUITTEMENT routé) sur faux agrégateur, et où la
/// FRONTIÈRE de permission (un non-superviseur ne voit jamais la supervision) est couverte par
/// PermissionGatedNavE2ETests (superviseur voit / operateur ne voit pas l'entrée) et DashboardE2ETests
/// (lecteur masqué). Anti-faux-vert : on n'affaiblit aucune assertion — le dashboard est réellement rendu
/// et l'absence du bandeau d'erreur prouve que la lecture cross-tenant a abouti.
/// </summary>
[Trait("Category", "E2E")]
public sealed class SupervisionDashboardE2ETests : KeycloakBaseE2ETest
{
    public SupervisionDashboardE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Superviseur_opens_supervision_and_sees_dashboard_render()
    {
        await LoginViaKeycloakAsync("superviseur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Navigation Liakont → Supervision (entrée gardée par liakont.supervision, posée par WEB01).
        await Page.Locator("[data-testid='nav-link-supervision']").ClickAsync();

        // L'intro de la page est rendue dès que la vue d'ensemble se charge (pas le bandeau d'erreur).
        var intro = Page.Locator("[data-testid='supervision-intro']");
        await intro.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await intro.IsVisibleAsync()).Should().BeTrue("le dashboard de supervision affiche son intro");

        // L'agrégation cross-tenant réelle a abouti : aucun bandeau d'erreur (un tenant injoignable serait
        // signalé ligne par ligne, jamais un échec global ici sur un backend sain).
        (await Page.Locator("[data-testid='supervision-error']").CountAsync())
            .Should().Be(0, "la vue d'ensemble de supervision se charge sans erreur contre le backend réel");
    }
}

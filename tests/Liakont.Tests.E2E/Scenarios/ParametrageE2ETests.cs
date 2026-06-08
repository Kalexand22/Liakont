namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de la page Paramétrage du tenant (WEB04b) : un utilisateur « lecture » se connecte
/// (flux Keycloak OIDC), ouvre la section Paramétrage depuis la navigation Liakont et voit la page
/// de synthèse (profil, fiscal, agents, comptes PA, table TVA, bouton de vérification d'intégrité).
/// Le rendu détaillé des états (alertes fiscales « décision en attente », secrets masqués, rapport
/// d'intégrité) est couvert par les tests bUnit (ParametrageView / Parametrage).
/// </summary>
[Trait("Category", "E2E")]
public sealed class ParametrageE2ETests : KeycloakBaseE2ETest
{
    public ParametrageE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Lecture_user_opens_parametrage_and_sees_settings_overview()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Navigation Liakont → Paramétrage (lien posé par WEB01, inconditionnel).
        await Page.Locator("[data-testid='nav-link-parametrage']").ClickAsync();

        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Paramétrage du tenant" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await heading.IsVisibleAsync()).Should().BeTrue("la page Paramétrage affiche son titre");

        // La synthèse se charge (pas le bandeau d'erreur) : le conteneur et le bouton d'intégrité sont visibles.
        var container = Page.Locator("[data-testid='liakont-parametrage']");
        await container.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await container.IsVisibleAsync()).Should().BeTrue("la synthèse de paramétrage est rendue");
        (await Page.Locator("[data-testid='parametrage-error']").CountAsync())
            .Should().Be(0, "le paramétrage du tenant se charge sans erreur");
        (await Page.Locator("[data-testid='parametrage-integrite-btn']").IsVisibleAsync())
            .Should().BeTrue("le bouton de vérification d'intégrité de l'archive est proposé");
    }
}

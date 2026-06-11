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
/// FIX208 ajoute la garde par permission des exports d'archive bout-en-bout (claims OIDC réels) :
/// « lecture » voit l'export d'audit (liakont.read) mais pas la réversibilité (liakont.settings) ;
/// « parametrage » voit la réversibilité.
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

        // FIX208 : l'export d'audit (liakont.read) est offert au rôle « lecture », mais PAS la
        // réversibilité complète du tenant (liakont.settings, absent du rôle « lecture »).
        (await Page.Locator("[data-testid='parametrage-audit-export']").IsVisibleAsync())
            .Should().BeTrue("l'export d'audit par période est offert au porteur de liakont.read");
        (await Page.Locator("[data-testid='parametrage-tenant-export']").CountAsync())
            .Should().Be(0, "la réversibilité du tenant exige liakont.settings, absent du rôle lecture");
    }

    [Fact]
    public async Task Parametrage_user_sees_the_tenant_reversibility_export()
    {
        // Le rôle « parametrage » cumule liakont.read + liakont.actions + liakont.settings.
        await LoginViaKeycloakAsync(username: "parametrage");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        await Page.Locator("[data-testid='nav-link-parametrage']").ClickAsync();

        var container = Page.Locator("[data-testid='liakont-parametrage']");
        await container.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Les deux exports sont offerts au porteur de liakont.settings.
        (await Page.Locator("[data-testid='parametrage-audit-export']").IsVisibleAsync())
            .Should().BeTrue("l'export d'audit reste offert (liakont.read)");
        var reversibilityBtn = Page.Locator("[data-testid='parametrage-tenant-export-btn']");
        (await reversibilityBtn.IsVisibleAsync())
            .Should().BeTrue("l'export de réversibilité est offert au porteur de liakont.settings");

        // La confirmation explicite expose le lien de téléchargement vers l'endpoint de réversibilité.
        await reversibilityBtn.ClickAsync();
        var confirm = Page.Locator("[data-testid='parametrage-tenant-export-confirm']");
        await confirm.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        (await confirm.GetAttributeAsync("href")).Should().Contain("/api/v1/tenant-export");
    }
}

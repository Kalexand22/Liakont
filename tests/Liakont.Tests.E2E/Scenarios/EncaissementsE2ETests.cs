namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de la page Encaissements (WEB06, e-reporting paiement F10 §2.4) : un utilisateur de rôle
/// <c>lecture</c> se connecte (flux Keycloak OIDC), ouvre la page Encaissements depuis la navigation maître
/// et la voit s'afficher. Le tenant E2E est vierge (aucun agrégat jour×taux n'est seedé) : la liste rend
/// donc son état vide explicite. Le rendu détaillé (badges de qualification fiscale, bandeaux capacité PA /
/// décision fiscale en attente, formatage français des montants) est couvert par les tests bUnit
/// (EncaissementsTests / PaymentAggregateColumnRegistryTests / PaymentAggregateStatusDisplayTests).
/// </summary>
[Trait("Category", "E2E")]
public sealed class EncaissementsE2ETests : KeycloakBaseE2ETest
{
    public EncaissementsE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Lecture_user_opens_encaissements_from_navigation()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Parcours opérateur : ouvrir la page Encaissements depuis la navigation maître Liakont
        // (lien posé par WEB01, inconditionnel).
        await Page.Locator("[data-testid='nav-link-encaissements']").ClickAsync();

        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Encaissements" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await heading.IsVisibleAsync()).Should().BeTrue("la page Encaissements s'affiche après navigation");

        // La page se charge sans bandeau d'erreur (lecture valide même pour un tenant non paramétré).
        (await Page.Locator("[data-testid='encaissements-error']").CountAsync())
            .Should().Be(0, "les encaissements se chargent sans erreur");

        // Tenant E2E vierge : aucun agrégat → la liste affiche un état vide explicite (et non une grille muette).
        var empty = Page.Locator("[data-testid='encaissements-empty']");
        await empty.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await empty.IsVisibleAsync()).Should().BeTrue("une période sans encaissement affiche un message explicite");
    }
}

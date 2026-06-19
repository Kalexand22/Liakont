namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de la page console des signatures (SIG10) : un utilisateur « lecture » se connecte (flux Keycloak
/// OIDC), ouvre la section Signatures depuis la navigation Liakont (lien posé par WEB01, gardé par liakont.read)
/// et voit la page de suivi (titre + formulaire de consultation + section fournisseurs). Le détail du statut,
/// de l'historique, de la preuve et des actions (déclencher/enregistrer/contester, gardées par liakont.actions)
/// est couvert par les tests bUnit (SignaturesTests) ; ce test prouve l'accessibilité bout-en-bout de la page
/// sous claims OIDC réels, sans dépendre de données de validation pré-amorcées.
/// </summary>
[Trait("Category", "E2E")]
public sealed class SignaturesE2ETests : KeycloakBaseE2ETest
{
    public SignaturesE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Lecture_user_opens_signatures_and_sees_the_lookup_page()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Navigation Liakont → Signatures (lien gardé par liakont.read, présent pour le rôle « lecture »).
        await Page.Locator("[data-testid='nav-link-signatures']").ClickAsync();

        var title = Page.Locator("[data-testid='signatures-title']");
        await title.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await title.IsVisibleAsync()).Should().BeTrue("la page Signatures affiche son titre");

        // Le formulaire de consultation (finalité + identifiant de document) et la section fournisseurs se rendent.
        (await Page.Locator("[data-testid='signatures-lookup']").IsVisibleAsync())
            .Should().BeTrue("le formulaire de consultation est proposé");
        (await Page.Locator("[data-testid='signatures-purpose']").IsVisibleAsync())
            .Should().BeTrue("le sélecteur de finalité est rendu");
        (await Page.Locator("[data-testid='signatures-providers']").CountAsync())
            .Should().Be(1, "la section des fournisseurs configurés est rendue");
    }
}

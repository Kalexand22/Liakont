namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de la page « Gestion des agents » (WEB09) : un utilisateur de rôle <c>parametrage</c> (porteur de
/// <c>liakont.settings</c>, via la projection rôle→permission OIDC d'IDN01) se connecte (flux Keycloak OIDC),
/// ouvre l'écran depuis la navigation Liakont (lien masqué sans la permission) et amorce l'enregistrement (le
/// dialogue présente le champ Nom). Un utilisateur <c>lecture</c> ne voit PAS le lien. Le parcours détaillé
/// (clé affichée une fois, confirmations de révocation / rotation, états Actif/Muet/Révoqué) est couvert par
/// les tests bUnit (AgentsTests / AgentRegisterDialogTests / AgentRevokeDialogTests / AgentRotateKeyDialogTests
/// / AgentManagementConsoleServiceTests) : ici on prouve le parcours réel (auth → nav → page rendue), comme
/// les autres écrans console, sans dépendre d'une écriture sur le tenant E2E vierge.
/// </summary>
[Trait("Category", "E2E")]
public sealed class AgentManagementE2ETests : KeycloakBaseE2ETest
{
    /// <summary>Utilisateur de rôle <c>parametrage</c> (liakont.read + actions + settings).</summary>
    private const string ParametrageUsername = "parametrage";

    public AgentManagementE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Parametrage_user_opens_agents_page_from_nav_and_can_start_registration()
    {
        await LoginViaKeycloakAsync(ParametrageUsername, DefaultPassword);

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Le lien « Agents » n'apparaît qu'avec liakont.settings (WEB01 + WEB09).
        await Page.Locator("[data-testid='nav-link-agents']").ClickAsync();

        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Gestion des agents" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await heading.IsVisibleAsync()).Should().BeTrue("la page Gestion des agents affiche son titre");

        (await Page.Locator("[data-testid='agents-error']").CountAsync())
            .Should().Be(0, "le parc d'agents se charge sans erreur");
        (await Page.Locator("[data-testid='agents-denied']").CountAsync())
            .Should().Be(0, "l'utilisateur de paramétrage a accès à la gestion des agents");

        // L'action d'enregistrement est proposée ; son ouverture présente le champ Nom (sans soumission :
        // le tenant E2E est vierge — le parcours mutant complet est couvert par les tests bUnit).
        var registerBtn = Page.Locator("[data-testid='agents-register-btn']");
        await registerBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await registerBtn.ClickAsync();

        var nameInput = Page.Locator("[data-testid='agent-register-name']");
        await nameInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await nameInput.IsVisibleAsync()).Should().BeTrue("le dialogue d'enregistrement présente le champ Nom");
    }

    [Fact]
    public async Task Lecture_user_does_not_see_the_agents_nav_link()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        (await Page.Locator("[data-testid='nav-link-agents']").CountAsync())
            .Should().Be(0, "le lien Agents est masqué sans liakont.settings (lecture seule)");

        // La navigation est bien rendue par ailleurs : le lien Paramétrage (inconditionnel) est présent.
        (await Page.Locator("[data-testid='nav-link-parametrage']").CountAsync())
            .Should().BeGreaterThan(0, "la navigation Liakont est rendue (lien Paramétrage inconditionnel)");
    }
}

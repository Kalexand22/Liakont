namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de la page Traitements (WEB04a, journal F10 §2.6) : un utilisateur de rôle <c>operateur</c>
/// (porteur de <c>liakont.actions</c>, requis pour les traitements — finding F5a / RLF03 ; un lecteur ne
/// voit plus l'entrée) se connecte, ouvre le journal des traitements depuis la navigation maître et le voit
/// s'afficher. Le tenant E2E est vierge (aucune exécution de pipeline n'est seedée) : le journal rend donc
/// son état vide explicite. Le formatage des colonnes, des badges et des compteurs sur données présentes
/// est couvert par les tests bUnit (TreatmentsTests / PipelineRunRowTests / PipelineRunColumnRegistryTests).
/// </summary>
[Trait("Category", "E2E")]
public sealed class TreatmentsE2ETests : KeycloakBaseE2ETest
{
    public TreatmentsE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Operateur_user_opens_treatments_journal_from_navigation()
    {
        await LoginViaKeycloakAsync("operateur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Parcours opérateur : ouvrir le journal des traitements depuis la navigation maître Liakont.
        await Page.Locator("[data-testid='nav-link-traitements']").ClickAsync();

        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Traitements" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await heading.IsVisibleAsync()).Should().BeTrue("la page Traitements s'affiche après navigation");

        // Tenant E2E vierge : aucune exécution → le journal affiche un message d'état vide explicite
        // (et non une grille muette), conformément à l'objectif « est-ce que ça a tourné cette nuit ? ».
        var empty = Page.Locator("[data-testid='traitements-empty']");
        await empty.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await empty.IsVisibleAsync()).Should().BeTrue("un journal sans traitement affiche un message explicite");
    }
}

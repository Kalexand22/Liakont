namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E du parcours d'accueil et de la navigation maître Liakont (WEB01) : un utilisateur de rôle
/// <c>lecture</c> se connecte (flux OIDC Keycloak), atterrit sur le tableau de bord et voit la section de
/// navigation Liakont. Les sections conditionnelles (Réconciliation, Supervision) sont masquées pour ce
/// profil sur un tenant sans pool de PDF. La navigation et le titre rendent indépendamment des données du
/// tenant ; le RENDU détaillé des widgets du tableau de bord (compteurs, état TVA, bandeau cadence) est
/// couvert par les tests bUnit (DashboardView / DashboardQueryService).
/// </summary>
[Trait("Category", "E2E")]
public sealed class DashboardE2ETests : KeycloakBaseE2ETest
{
    public DashboardE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Lecture_user_lands_on_dashboard_and_sees_liakont_navigation()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();
        (await shell.Shell.IsVisibleAsync()).Should().BeTrue("le shell s'affiche pour un utilisateur authentifié");

        // L'accueil "/" est le tableau de bord (WEB01).
        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Tableau de bord" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await heading.IsVisibleAsync()).Should().BeTrue("la page d'accueil affiche le tableau de bord");

        // Navigation maître Liakont : les sections inconditionnelles sont présentes.
        foreach (var link in new[] { "documents", "encaissements", "traitements", "parametrage" })
        {
            (await Page.Locator($"[data-testid='nav-link-{link}']").IsVisibleAsync())
                .Should().BeTrue($"la section « {link} » figure dans la navigation Liakont");
        }

        // Sections conditionnelles masquées : lecteur (pas de supervision) sur un tenant sans pool PDF.
        (await Page.Locator("[data-testid='nav-link-supervision']").CountAsync())
            .Should().Be(0, "la supervision est réservée au superviseur");
        (await Page.Locator("[data-testid='nav-link-reconciliation']").CountAsync())
            .Should().Be(0, "la réconciliation n'apparaît que si l'agent alimente un pool de PDF");
    }
}

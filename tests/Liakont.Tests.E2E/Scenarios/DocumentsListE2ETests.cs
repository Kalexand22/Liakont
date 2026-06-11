namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de la page Documents (WEB02) : un utilisateur de rôle <c>lecture</c> se connecte (flux OIDC
/// Keycloak), navigue depuis la section « Documents » de la navigation maître Liakont et voit la vue
/// centrale — titre, barre de filtres métier (F10 §2.1), synthèse par état et grille (gabarit
/// DeclaredListPage). Sans <c>liakont.actions</c>, les actions d'envoi (WEB05) sont MASQUÉES. Le
/// comportement détaillé des filtres et des compteurs est couvert par les tests bUnit ; ici on prouve
/// le parcours réel (navigation → page rendue) de bout en bout. La vue/déclenchement des actions d'envoi
/// par un opérateur est couvert par DocumentSendActionsE2ETests.
/// </summary>
[Trait("Category", "E2E")]
public sealed class DocumentsListE2ETests : KeycloakBaseE2ETest
{
    public DocumentsListE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Lecture_user_navigates_to_documents_page_and_sees_the_central_view()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Navigation réelle depuis la section « Documents » de la nav maître Liakont (livrée en WEB01).
        await Page.Locator("[data-testid='nav-link-documents']").ClickAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/documents", System.StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 30_000 });

        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Documents" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await heading.IsVisibleAsync()).Should().BeTrue("la page Documents affiche son titre");

        // Barre de filtres métier (F10 §2.1) : période + état + type. Elle s'affiche APRÈS le chargement
        // asynchrone du périmètre sur le circuit interactif — on l'attend explicitement (anti-course).
        var filters = Page.Locator("[data-testid='documents-filters']");
        await filters.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await filters.IsVisibleAsync())
            .Should().BeTrue("la barre de filtres période/état/type s'affiche");
        (await Page.Locator("[data-testid='documents-filter-state']").IsVisibleAsync())
            .Should().BeTrue("le filtre État est présent");
        (await Page.Locator("[data-testid='documents-filter-type']").IsVisibleAsync())
            .Should().BeTrue("le filtre Type est présent");

        // Synthèse permanente par état (pastille « Tous » toujours présente).
        (await Page.Locator("[data-testid='doc-counts-all']").IsVisibleAsync())
            .Should().BeTrue("la synthèse par état s'affiche au-dessus de la liste");

        // FIX206 : la barre d'outils du gabarit commun des listes (DeclaredListPage → StratumDataGrid)
        // expose une icône Rafraîchir (relance la requête en conservant filtres/pagination).
        var refresh = Page.Locator("[data-testid='refresh-btn']");
        await refresh.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await refresh.IsVisibleAsync())
            .Should().BeTrue("la liste expose une icône Rafraîchir (FIX206)");

        // Actions d'envoi MASQUÉES pour un utilisateur lecture (WEB05) : sans liakont.actions, ni « Tout
        // envoyer », ni « Lancer un traitement », ni barre d'actions groupées « Envoyer la sélection ».
        (await Page.Locator("[data-testid='documents-send-all']").CountAsync())
            .Should().Be(0, "sans liakont.actions, « Tout envoyer » est masqué");
        (await Page.Locator("[data-testid='documents-trigger-run']").CountAsync())
            .Should().Be(0, "sans liakont.actions, « Lancer un traitement » est masqué");
    }
}

namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de la page « Paramétrage comptable — Table TVA » (WEB07a) : un utilisateur de rôle
/// <c>parametrage</c> (porteur de <c>liakont.settings</c>) se connecte (flux OIDC Keycloak), ouvre le
/// Paramétrage puis suit le lien « Voir / éditer la table TVA » (posé par WEB04b) et atterrit sur la
/// page de la table — titre, conteneur et section d'état de validation rendus, sans bandeau d'erreur.
/// Le détail (états de validation, garde de confirmation, changelog) est couvert par les tests bUnit ;
/// ici on prouve le parcours réel (navigation → page rendue) de bout en bout.
/// </summary>
[Trait("Category", "E2E")]
public sealed class TableTvaE2ETests : KeycloakBaseE2ETest
{
    /// <summary>Utilisateur de rôle <c>parametrage</c> (liakont.read + actions + settings).</summary>
    private const string ParametrageUsername = "parametrage";

    public TableTvaE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Parametrage_user_navigates_from_settings_to_the_tva_table_page()
    {
        await LoginViaKeycloakAsync(ParametrageUsername, DefaultPassword);

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Navigation Liakont → Paramétrage (lien posé par WEB01, inconditionnel).
        await Page.Locator("[data-testid='nav-link-parametrage']").ClickAsync();

        var parametrageContainer = Page.Locator("[data-testid='liakont-parametrage']");
        await parametrageContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Lien « Voir / éditer la table TVA » (WEB04b) → page table TVA (WEB07a).
        await Page.Locator("[data-testid='parametrage-tva-link']").ClickAsync();

        await Page.WaitForURLAsync(
            url => url.Contains("/parametrage/table-tva", System.StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 30_000 });

        // Attend le conteneur SPÉCIFIQUE de la nouvelle page (prouve le swap du composant Blazor avant
        // toute assertion — l'ancien h1 reste brièvement dans le DOM en navigation interactive).
        var container = Page.Locator("[data-testid='liakont-tva-table']");
        await container.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await container.IsVisibleAsync()).Should().BeTrue("la vue de la table TVA est rendue");

        // Titre de la page (le h1 « Paramétrage comptable — Table TVA » ; pas la carte « Table de mapping TVA »).
        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Table TVA" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await heading.IsVisibleAsync()).Should().BeTrue("la page Table TVA affiche son titre");

        (await Page.Locator("[data-testid='table-tva-error']").CountAsync())
            .Should().Be(0, "la table TVA se charge sans erreur");

        // La section d'état de validation est présente (✅ / ⚠️ / « aucune table »).
        (await Page.Locator("[data-testid='table-tva-validation']").CountAsync())
            .Should().BeGreaterThan(0, "la section d'état de validation est rendue");
    }

    /// <summary>
    /// WEB07b : vérifie que la page table TVA se charge sans erreur pour un utilisateur de paramétrage
    /// lorsque le chemin des options d'édition statiques (listes fermées) est actif. Seul ce chemin est
    /// exercé ici de bout en bout : le tenant E2E est vierge et l'utilisateur n'a pas de société rattachée
    /// (claim <c>company_id</c> absent du realm), donc <c>GetTableAsync</c> retourne immédiatement avec
    /// <c>Coverage = null</c> sans déclencher la requête de couverture — la page affiche l'état vide
    /// explicite (« Aucune table TVA paramétrée ») sans bandeau d'erreur. Le chemin couverture (qui
    /// nécessite une société résolue) et le parcours détaillé d'édition (listes fermées, création,
    /// invalidation) sont couverts par les tests bUnit (TvaRuleEditorTests / TableTvaViewTests /
    /// TableTvaTests), pas par ce test E2E.
    /// </summary>
    [Fact]
    public async Task Parametrage_user_loads_the_tva_table_edit_page_without_error()
    {
        await LoginViaKeycloakAsync(ParametrageUsername, DefaultPassword);

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Navigation directe vers la page d'édition de la table TVA (WEB07a/b).
        await Page.GotoAsync($"{BaseUrl}/parametrage/table-tva");

        var container = Page.Locator("[data-testid='liakont-tva-table']");
        await container.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Seul le chemin des options d'édition statiques (pas de société = pas de requête couverture) est exercé ici.
        (await Page.Locator("[data-testid='table-tva-error']").CountAsync())
            .Should().Be(0, "le chargement étendu (édition) reste sans erreur");

        // Tenant vierge / sans société : état vide explicite, aucun contrôle d'édition (pas de table).
        var empty = Page.Locator("[data-testid='table-tva-none']");
        await empty.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await empty.IsVisibleAsync()).Should().BeTrue("sans table paramétrée, l'édition n'est pas proposée");

        (await Page.Locator("[data-testid='table-tva-create-btn']").CountAsync())
            .Should().Be(0, "aucun bouton de création tant qu'aucune table n'existe");
    }
}

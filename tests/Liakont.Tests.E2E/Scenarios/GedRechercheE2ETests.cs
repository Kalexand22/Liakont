namespace Liakont.Tests.E2E.Scenarios;

using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Tests.E2E;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// E2E (GED09a, F19 §6.7) de la page portail « Recherche documentaire » <c>/ged/recherche</c> : prouve de
/// bout en bout, sous Keycloak/OIDC réel, que la surface de consultation GED est atteignable par un rôle
/// porteur de <c>liakont.ged.read</c> (le superviseur est un rôle éditeur → GED06) — l'entrée de navigation
/// « Recherche GED » est visible ET la page rend sa vue-pure dans son état initial (invite de recherche). La
/// policy de page <c>[Authorize(Policy = liakont.ged.read)]</c> et le pont rôle→permission (IDN01) sont donc
/// exercés réellement. Anti-faux-vert : aucun état masqué, l'invite est réellement rendue.
/// </summary>
/// <remarks>
/// Catégorie E2E : exécuté par la suite Playwright (<c>tools/run-e2e.ps1</c>, conteneurs Keycloak +
/// PostgreSQL), jamais par <c>verify-fast</c>/<c>run-tests</c>. La preuve EXÉCUTÉE dans le pipeline de GED09a
/// (verify-fast + run-tests) est portée par les tests bUnit de la vue-pure et les tests unitaires du seam de
/// composition. L'état initial (invite) ne dépend d'aucun document GED seedé.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class GedRechercheE2ETests : KeycloakBaseE2ETest
{
    private const string GedSearchNavTestId = "nav-link-ged-recherche";

    public GedRechercheE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Superviseur_Sees_The_Ged_Search_Nav_And_Can_Open_The_Page()
    {
        await LoginViaKeycloakAsync("superviseur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // L'entrée de navigation « Recherche GED » est gardée par liakont.ged.read (GED06 : accordée aux rôles
        // éditeur, dont superviseur) : elle est réellement rendue.
        (await Page.GetByTestId(GedSearchNavTestId).IsVisibleAsync())
            .Should().BeTrue("le superviseur porte liakont.ged.read → l'entrée de navigation Recherche GED est visible");

        // La page /ged/recherche est autorisée par la policy liakont.ged.read et rend sa vue-pure dans l'état
        // initial (invite de recherche), sans dépendre d'un document GED seedé.
        await Page.GotoAsync($"{BaseUrl}/ged/recherche");

        (await Page.GetByTestId("ged-search-input").IsVisibleAsync())
            .Should().BeTrue("la page de recherche documentaire est autorisée et rend le champ de recherche");
        (await Page.GetByTestId("ged-search-hint").IsVisibleAsync())
            .Should().BeTrue("avant toute recherche, l'invite FR est affichée (aucun document seedé requis)");
    }
}

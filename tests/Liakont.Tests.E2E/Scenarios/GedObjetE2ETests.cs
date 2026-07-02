namespace Liakont.Tests.E2E.Scenarios;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// E2E (GED09c, F19 §6.7) de la page portail « Exploration d'objet » <c>/ged/objet/{entityType}/{id}</c> :
/// prouve de bout en bout, sous Keycloak/OIDC réel, que la surface d'exploration de graphe est atteignable par un
/// rôle porteur de <c>liakont.ged.read</c> (le superviseur est un rôle éditeur → GED06) — la page rend sa vue-pure
/// (en-tête d'exploration) dans son état initial. La policy de page <c>[Authorize(Policy = liakont.ged.read)]</c>
/// et le pont rôle→permission (IDN01) sont donc exercés réellement. Anti-faux-vert : l'en-tête est réellement
/// rendu (la page n'est ni un 403 ni un blanc).
/// </summary>
/// <remarks>
/// Catégorie E2E : exécuté par la suite Playwright (<c>tools/run-e2e.ps1</c>, conteneurs Keycloak + PostgreSQL),
/// jamais par <c>verify-fast</c>/<c>run-tests</c>. La preuve EXÉCUTÉE dans le pipeline de GED09c (verify-fast +
/// run-tests) est portée par les tests bUnit de la vue-pure/page et les tests unitaires du seam de composition.
/// L'exploration cible une entité racine inconnue (aucun graphe GED seedé n'est requis) : la page est autorisée et
/// rend son enveloppe (en-tête + résultat borné côté serveur, vide ou message d'indisponibilité — jamais un dump).
/// </remarks>
[Trait("Category", "E2E")]
public sealed class GedObjetE2ETests : KeycloakBaseE2ETest
{
    public GedObjetE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Superviseur_Can_Open_The_Object_Exploration_Page()
    {
        await LoginViaKeycloakAsync("superviseur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // La page /ged/objet/{type}/{id} est autorisée par la policy liakont.ged.read (GED06 : accordée aux rôles
        // éditeur, dont superviseur) et rend sa vue-pure d'exploration — la racine ciblée est inconnue, donc aucun
        // graphe GED seedé n'est requis (le résultat borné côté serveur est vide ou indisponible, jamais un dump).
        var rootId = Guid.NewGuid();
        await Page.GotoAsync($"{BaseUrl}/ged/objet/entreprise/{rootId}");

        // Le prerender est DÉSACTIVÉ sur cette page (GDF05, comme GedDocument) : l'enveloppe d'exploration n'est
        // rendue qu'APRÈS connexion du circuit interactif — on ATTEND donc sa visibilité (un WaitForAsync réussi
        // VAUT l'assertion), au lieu d'un IsVisibleAsync ponctuel qui verrait le HTML initial (sans prerender) vide.
        await Page.GetByTestId("ged-graph")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await Page.GetByTestId("ged-graph-heading")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    }
}

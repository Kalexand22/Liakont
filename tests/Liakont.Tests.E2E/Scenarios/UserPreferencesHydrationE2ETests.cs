namespace Liakont.Tests.E2E.Scenarios;

using System.Threading.Tasks;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

/// <summary>
/// Tests E2E des préférences utilisateur (RBF08 — RB24/RB25) : (1) la densité par défaut EFFECTIVE
/// est <c>standard</c> pour une session vierge (et non <c>compact</c>, le défaut socle), et (2) la
/// préférence persistée en base est ré-appliquée dès le login même sur un navigateur « neuf »
/// (localStorage vide) — donc elle suit l'utilisateur quel que soit le navigateur.
/// </summary>
[Trait("Category", "E2E")]
public sealed class UserPreferencesHydrationE2ETests : KeycloakBaseE2ETest
{
    private const string UserDensityStandard = "standard";
    private const string UserDensityCompact = "compact";

    public UserPreferencesHydrationE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Default_density_is_standard_and_persisted_choice_is_rehydrated_across_browsers()
    {
        var html = Page.Locator("html");
        await LoginViaKeycloakAsync();

        // ── Setup idempotent GARANTI : repartir d'une base connue (densité persistée = « standard », le défaut). ──
        // L'utilisateur de test du realm est PARTAGÉ : la justesse de l'étape 1 ne doit JAMAIS dépendre d'un
        // teardown best-effort du run précédent (qui a pu être interrompu entre « pose compact » et sa restauration,
        // laissant « compact » en base — l'étape 1 échouerait alors à tort). On normalise ICI, en setup garanti
        // (PAS d'exception avalée : si le reset échoue, le test échoue franchement plutôt que sur une base inconnue).
        await ResetPersistedDensityToStandardAsync(html);

        try
        {
            // ── 1. Navigateur « neuf » (localStorage vidé) : la densité effective est « standard » (RB25), pas le
            //    défaut socle « compact » — la base (standard) est ré-hydratée au login par l'hydrateur de shell. ──
            await Page.EvaluateAsync("() => localStorage.clear()");
            var shell = GetShellPage();
            await shell.GotoHomeAsync();
            await shell.WaitForShellAsync();
            await Expect(html).ToHaveAttributeAsync(
                "data-density", UserDensityStandard, new() { Timeout = 30_000 });

            // ── 2. L'utilisateur choisit « compact » → appliqué en live ET persisté en base. ──
            await Page.GotoAsync($"{BaseUrl}/settings/preferences");
            await Page.Locator("[data-testid='user-preferences-panel']").WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await Page.Locator("[data-testid='pref-density-compact']").ClickAsync();
            await Expect(html).ToHaveAttributeAsync(
                "data-density", UserDensityCompact, new() { Timeout = 30_000 });

            // Preuve de la persistance en base : un rechargement du panneau (lecture base au OnInitialized)
            // ré-affiche « compact » comme sélectionné.
            await Page.ReloadAsync();
            await Page.Locator("[data-testid='user-preferences-panel']").WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await Expect(Page.Locator("[data-testid='pref-density-compact']"))
                .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 30_000 });

            // ── 3. Navigateur « neuf » : localStorage vidé → la base (compact) est ré-hydratée au login. ──
            // Sans RBF08, la couche live retombait sur le défaut (standard) car localStorage était vide ;
            // l'hydrateur de shell relit la base et ré-applique « compact ».
            await Page.EvaluateAsync("() => localStorage.clear()");
            await shell.GotoHomeAsync();
            await shell.WaitForShellAsync();
            await Expect(html).ToHaveAttributeAsync(
                "data-density", UserDensityCompact, new() { Timeout = 30_000 });
        }
        finally
        {
            // Teardown de COURTOISIE (best-effort) : remettre « standard » pour ne pas laisser la densité de
            // l'utilisateur partagé à « compact ». La justesse du PROCHAIN run ne dépend PAS de ce nettoyage —
            // le setup idempotent ci-dessus garantit déjà la base ; ce bloc n'est qu'une politesse, donc on
            // avale son éventuelle exception (sans masquer celle, prioritaire, du corps du test).
            try
            {
                await ResetPersistedDensityToStandardAsync(html);
            }
            catch (System.Exception)
            {
                // Best-effort : ne jamais masquer l'exception d'origine du test.
            }
        }
    }

    /// <summary>Normalise la densité PERSISTÉE de l'utilisateur courant à « standard » (le défaut) via le panneau
    /// de réglages, et attend la confirmation live. Idempotent (cliquer « standard » quand c'est déjà le cas est
    /// sans effet). Utilisé en setup GARANTI (base connue) et en teardown de courtoisie.</summary>
    private async Task ResetPersistedDensityToStandardAsync(ILocator html)
    {
        await Page.GotoAsync($"{BaseUrl}/settings/preferences");
        await Page.Locator("[data-testid='user-preferences-panel']").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await Page.Locator("[data-testid='pref-density-standard']").ClickAsync();
        await Expect(html).ToHaveAttributeAsync(
            "data-density", UserDensityStandard, new() { Timeout = 30_000 });
    }
}

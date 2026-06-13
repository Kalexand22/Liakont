namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Tests E2E de mise en page du shell (FIX306) — gardes de NON-RÉGRESSION de l'invariant du shell
/// <c>ErpShellLayout</c> : le DOCUMENT ne défile jamais (la barre latérale reste figée et entière) et
/// c'est la zone <c>.erp-main</c> qui défile en interne. Le défaut signalé en recette run 3 sur une
/// page longue (/parametrage/alertes, estimé GLOBAL au shell, décision F6) — « scroll au-delà du bas
/// de page + barre latérale tronquée en haut » — n'est plus observable dans le code courant (la nav
/// socle volumineuse de l'époque a été retirée par FIX209/FIX303, et la propriété « le document ne
/// défile pas » est garantie). FIX306 rend cette propriété EXPLICITE via le verrou <c>overflow:
/// hidden</c> scopé aux pages shell (<c>:has(.erp-shell)</c>) ; ces tests la VERROUILLENT — sur du
/// contenu plus haut que le viewport et sous contrainte — pour empêcher toute régression future.
/// </summary>
[Trait("Category", "E2E")]
public sealed class ShellLayoutE2ETests : KeycloakBaseE2ETest
{
    private const int ViewportWidth = 1280;
    private const int ViewportHeight = 720;

    private readonly ITestOutputHelper _output;

    public ShellLayoutE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
        _output = output;
    }

    /// <summary>
    /// Garde de confinement : un contenu plus haut que le viewport défile À L'INTÉRIEUR de
    /// <c>.erp-main</c> (et reste atteignable), sans faire défiler le document ni bouger la barre latérale.
    /// </summary>
    [Fact]
    public async Task Tall_content_scrolls_inside_main_without_scrolling_the_document()
    {
        await Page.SetViewportSizeAsync(ViewportWidth, ViewportHeight);
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();
        var main = shell.Main;
        var nav = shell.Nav;
        await main.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Sonde plus haute que le viewport dans la zone de contenu (isole le comportement du shell).
        await main.EvaluateAsync(
            "(el, h) => { const p = document.createElement('div'); p.dataset.testid = 'layout-probe';"
            + " p.style.height = h + 'px'; el.appendChild(p); }",
            2000);
        var probe = Page.Locator("[data-testid='layout-probe']");

        await Page.EvaluateAsync("() => window.scrollTo(0, 100000)");
        var windowScrollY = await Page.EvaluateAsync<double>("() => window.scrollY");
        var navBox = await nav.BoundingBoxAsync();
        var mainScrollHeight = await main.EvaluateAsync<double>("el => el.scrollHeight");
        var mainClientHeight = await main.EvaluateAsync<double>("el => el.clientHeight");
        await main.EvaluateAsync("el => el.scrollTo(0, el.scrollHeight)");
        var mainScrollTop = await main.EvaluateAsync<double>("el => el.scrollTop");
        var probeBox = await probe.BoundingBoxAsync();

        _output.WriteLine($"[containment] window.scrollY={windowScrollY}");
        _output.WriteLine($"[containment] nav={(navBox is null ? "null" : $"y={navBox.Y} h={navBox.Height}")}");
        _output.WriteLine($"[containment] main scrollHeight={mainScrollHeight} clientHeight={mainClientHeight} scrollTop={mainScrollTop}");
        _output.WriteLine($"[containment] probe={(probeBox is null ? "null" : $"y={probeBox.Y} h={probeBox.Height}")}");

        using var assertions = new FluentAssertions.Execution.AssertionScope();
        windowScrollY.Should().BeLessThan(2, "le document ne défile pas (le shell borne le scroll au contenu)");
        navBox.Should().NotBeNull("la barre latérale est rendue");
        navBox!.Y.Should().BeGreaterThanOrEqualTo(-2, "la barre latérale reste ancrée en haut du viewport");
        (navBox.Y + navBox.Height).Should().BeLessThanOrEqualTo(
            ViewportHeight + 2, "la barre latérale reste entièrement dans le viewport");
        mainScrollHeight.Should().BeGreaterThan(
            mainClientHeight, ".erp-main défile en interne (il ne grandit pas pour épouser son contenu)");
        mainScrollTop.Should().BeGreaterThan(0, ".erp-main a effectivement défilé jusqu'au bas du contenu");
        probeBox.Should().NotBeNull("la sonde de contenu est rendue");
        (probeBox!.Y + probeBox.Height).Should().BeLessThanOrEqualTo(
            ViewportHeight + 2, "le bas du contenu est atteignable après défilement de .erp-main");
    }

    /// <summary>
    /// Garde de l'invariant sous contrainte : même un élément qui ÉTEND la zone scrollable du document
    /// (le « scroll au-delà du bas de page » du run 3) ne doit PAS rendre le document scrollable ni
    /// emporter la barre latérale vers le haut (« menu tronqué en haut »). Verrouille la propriété que
    /// FIX306 rend explicite (<c>overflow: hidden</c> scopé aux pages shell), contre toute régression future.
    /// </summary>
    [Fact]
    public async Task Element_extending_document_scroll_area_does_not_scroll_the_document()
    {
        await Page.SetViewportSizeAsync(ViewportWidth, ViewportHeight);
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();
        var nav = shell.Nav;

        // Étend la zone scrollable du document bien au-delà du viewport (élément positionné dans le
        // bloc conteneur initial) : reproduit « scroll au-delà du bas de page » du run 3.
        await Page.EvaluateAsync(
            "() => { const d = document.createElement('div'); d.dataset.testid = 'doc-overflow-probe';"
            + " d.style.position = 'absolute'; d.style.top = '300vh'; d.style.left = '0';"
            + " d.style.width = '1px'; d.style.height = '1px'; document.body.appendChild(d); }");

        await Page.EvaluateAsync("() => window.scrollTo(0, 100000)");
        var windowScrollY = await Page.EvaluateAsync<double>("() => window.scrollY");
        var navBox = await nav.BoundingBoxAsync();

        _output.WriteLine($"[doc-lock] window.scrollY={windowScrollY}");
        _output.WriteLine($"[doc-lock] nav={(navBox is null ? "null" : $"y={navBox.Y} h={navBox.Height}")}");

        using var assertions = new FluentAssertions.Execution.AssertionScope();
        windowScrollY.Should().BeLessThan(
            2, "le document ne défile pas même si un élément étend sa zone scrollable (verrou overflow FIX306)");
        navBox.Should().NotBeNull("la barre latérale est rendue");
        navBox!.Y.Should().BeGreaterThanOrEqualTo(
            -2, "la barre latérale n'est jamais emportée vers le haut (« menu tronqué en haut » du run 3)");
        (navBox.Y + navBox.Height).Should().BeLessThanOrEqualTo(
            ViewportHeight + 2, "la barre latérale reste entièrement dans le viewport");
    }
}

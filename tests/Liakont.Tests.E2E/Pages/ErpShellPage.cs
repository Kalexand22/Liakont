namespace Liakont.Tests.E2E.Pages;

using Microsoft.Playwright;

/// <summary>
/// Page Object Model du shell applicatif Liakont rendu après authentification
/// (layout <c>ErpShellLayout</c> — conteneur <c>.erp-shell</c> + navigation + contenu).
/// Sert de base de navigation pour les items <c>blazor-page-item</c> (WEB*, SUP*, OPS03).
/// </summary>
public sealed class ErpShellPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public ErpShellPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    /// <summary>Conteneur racine du shell connecté (présent sur toutes les pages authentifiées).</summary>
    public ILocator Shell => _page.Locator(".erp-shell");

    /// <summary>Barre de navigation latérale du shell.</summary>
    public ILocator Nav => _page.Locator(".erp-shell .erp-nav, .erp-shell nav").First;

    /// <summary>Zone de contenu principal du shell.</summary>
    public ILocator Main => _page.Locator(".erp-shell .erp-main");

    /// <summary>Navigue vers l'accueil (route protégée "/").</summary>
    public async Task GotoHomeAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>Attend que le shell connecté soit visible.</summary>
    public async Task WaitForShellAsync(float timeout = 30_000)
    {
        await Shell.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeout });
    }
}

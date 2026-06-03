namespace Liakont.Tests.E2E;

using Microsoft.Playwright;
using Xunit;

/// <summary>
/// Collection fixture qui initialise Playwright et lance une instance Chromium partagée.
/// Chaque test obtient un contexte de navigateur isolé via <see cref="NewPageAsync"/>.
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }

    /// <summary>
    /// Crée un contexte de navigateur isolé et une page pour un seul test.
    /// L'appelant est responsable de disposer le contexte après le test.
    /// </summary>
    public async Task<(IBrowserContext Context, IPage Page)> NewPageAsync()
    {
        if (_browser is null)
        {
            throw new InvalidOperationException("PlaywrightFixture n'a pas été initialisée.");
        }

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            IgnoreHTTPSErrors = true,
        });

        context.SetDefaultTimeout(30_000);

        var page = await context.NewPageAsync();
        return (context, page);
    }
}

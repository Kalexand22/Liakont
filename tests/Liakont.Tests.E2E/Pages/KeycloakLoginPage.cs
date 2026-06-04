namespace Liakont.Tests.E2E.Pages;

using Microsoft.Playwright;

/// <summary>
/// Page Object Model de la page de login Keycloak.
/// Utilise les sélecteurs du thème par défaut de Keycloak 26.
/// </summary>
public sealed class KeycloakLoginPage
{
    private readonly IPage _page;

    public KeycloakLoginPage(IPage page)
    {
        _page = page;
    }

    public ILocator UsernameInput => _page.Locator("#username");

    public ILocator PasswordInput => _page.Locator("#password");

    public ILocator SubmitButton => _page.Locator("#kc-login");

    public ILocator ErrorAlert => _page.Locator(".alert-error, #input-error, .kc-feedback-text");

    /// <summary>Attend que le formulaire de login Keycloak soit complètement chargé.</summary>
    public async Task WaitForPageAsync(float timeout = 30_000)
    {
        await UsernameInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    /// <summary>Remplit les identifiants et soumet le formulaire de login Keycloak.</summary>
    public async Task LoginAsync(string username, string password)
    {
        await UsernameInput.FillAsync(username);
        await PasswordInput.FillAsync(password);
        await SubmitButton.ClickAsync();
    }

    /// <summary>Retourne le texte du message d'erreur, ou null si aucune erreur n'est visible.</summary>
    public async Task<string?> GetErrorMessageAsync()
    {
        var first = ErrorAlert.First;
        if (await first.CountAsync() == 0)
        {
            return null;
        }

        return await first.TextContentAsync();
    }
}

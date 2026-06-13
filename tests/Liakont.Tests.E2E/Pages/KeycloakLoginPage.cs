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

    /// <summary>Champ de saisie du code TOTP (formulaire OTP du thème Keycloak 26, second facteur).</summary>
    public ILocator OtpInput => _page.Locator("#otp");

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

    /// <summary>Attend l'affichage du formulaire OTP (second facteur) après la soumission du mot de passe.</summary>
    public async Task WaitForOtpAsync(float timeout = 30_000)
    {
        await OtpInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    /// <summary>Saisit le code TOTP et soumet le formulaire OTP.</summary>
    public async Task SubmitOtpAsync(string code)
    {
        await OtpInput.FillAsync(code);
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

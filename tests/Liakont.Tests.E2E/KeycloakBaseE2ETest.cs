namespace Liakont.Tests.E2E;

using Liakont.Tests.E2E.Pages;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Classe de base pour les tests E2E Keycloak (OIDC). Fournit une <see cref="Page"/> isolée,
/// la <see cref="BaseUrl"/>, et un helper <see cref="LoginViaKeycloakAsync"/> qui traverse la
/// page de login Keycloak. Les identifiants par défaut sont l'utilisateur de rôle
/// <c>lecture</c> du realm <c>liakont-dev</c> seedé par SOL01.
/// </summary>
[Collection(KeycloakE2ECollection.Name)]
public abstract class KeycloakBaseE2ETest : IAsyncLifetime
{
    /// <summary>
    /// Utilisateur de test par défaut (rôle <c>lecture</c>, realm liakont-dev).
    /// Le username Keycloak est un identifiant court (et non l'email) car le value object
    /// <c>Username</c> du module Identity exige 3-50 caractères alphanumériques + underscores
    /// (INV-IDENTITY-007) — un <c>preferred_username</c> de type email est rejeté au sync OIDC.
    /// </summary>
    public const string DefaultUsername = "lecture";

    /// <summary>Mot de passe des utilisateurs de test du realm liakont-dev (SOL01).</summary>
    public const string DefaultPassword = "Test@1234";

    private readonly PlaywrightFixture _playwright;
    private readonly ITestOutputHelper _output;
    private IBrowserContext? _context;

    protected KeycloakBaseE2ETest(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
    {
        Factory = factory;
        _playwright = playwright;
        _output = output;
        BaseUrl = factory.BaseUrl;
    }

    protected KeycloakE2EWebFactory Factory { get; }

    protected IPage Page { get; private set; } = null!;

    protected string BaseUrl { get; }

    public virtual async Task InitializeAsync()
    {
        (_context, Page) = await _playwright.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await CaptureScreenshotOnFailureAsync();

        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }

    /// <summary>Crée un POM <see cref="KeycloakLoginPage"/> lié à la page courante.</summary>
    protected KeycloakLoginPage GetKeycloakLoginPage() => new(Page);

    /// <summary>Crée un POM <see cref="ErpShellPage"/> lié à la page courante.</summary>
    protected ErpShellPage GetShellPage() => new(Page, BaseUrl);

    /// <summary>
    /// Flux OIDC complet : navigue vers /login, suit la redirection vers Keycloak, remplit le
    /// formulaire de login Keycloak, soumet et attend le chargement de l'application.
    /// </summary>
    protected async Task LoginViaKeycloakAsync(
        string username = DefaultUsername,
        string password = DefaultPassword)
    {
        // Navigue vers /login — l'application redirige vers l'endpoint authorize de Keycloak.
        await Page.GotoAsync($"{BaseUrl}/login");

        // Attend le formulaire de login Keycloak.
        var kcPage = GetKeycloakLoginPage();
        await kcPage.WaitForPageAsync();

        // Remplit les identifiants et soumet.
        await kcPage.LoginAsync(username, password);

        // Attend la fin de la chaîne de callback OIDC :
        // Keycloak → /signin-oidc → UserSyncService → sign-in cookie → redirection vers l'accueil.
        await Page.WaitForURLAsync(
            url => url.StartsWith(BaseUrl, StringComparison.Ordinal) && !url.Contains("/signin-oidc") && !url.Contains("/login"),
            new PageWaitForURLOptions { Timeout = 30_000 });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private async Task CaptureScreenshotOnFailureAsync()
    {
        try
        {
            var dir = Path.Combine("test-results", "screenshots");
            Directory.CreateDirectory(dir);

            var testName = GetType().Name;
            var path = Path.Combine(dir, $"{testName}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png");

            await Page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
            _output.WriteLine($"Capture d'écran enregistrée : {path}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Échec de la capture d'écran : {ex.Message}");
        }
    }
}

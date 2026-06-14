namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de l'écran « Clients » (OPS03 lot C) : un superviseur navigue par le SOUS-MENU
/// Supervision → Clients (nouvelle branche de nav), voit la liste se charger sans erreur contre le
/// backend réel, ouvre l'assistant « Nouveau client » et exerce la validation de FORME de l'étape
/// profil. BORNAGE ACTÉ : la création réelle d'un tenant (base + registre + realm) et le flux
/// utilisateur/agent ne sont PAS exerçables ici — la factory E2E ne configure ni Keycloak admin ni
/// AdminSeed (pas de SystemAdmin en base système, prérequis de SeedTenantAdminAsync) ; ces chemins
/// sont couverts de bout en bout par <c>ClientProvisioningConsoleIntegrationTests</c> (base réelle
/// Testcontainers + realm fake) et <c>TenantUserAdminEndpointTests</c>.
/// </summary>
[Trait("Category", "E2E")]
public sealed class ClientsE2ETests : KeycloakBaseE2ETest
{
    public ClientsE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Superviseur_opens_the_clients_screen_and_starts_the_wizard()
    {
        await LoginViaKeycloakAsync("superviseur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Sous-menu Supervision (OPS03) : la feuille « Clients » est dépliée par défaut (niveaux 0+1).
        await Page.Locator("[data-testid='nav-link-clients']").ClickAsync();

        // La liste se charge sans bandeau d'erreur (la composition registre + scopes par tenant aboutit).
        var createBtn = Page.Locator("[data-testid='clients-create-btn']");
        await createBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await Page.Locator("[data-testid='clients-error']").CountAsync())
            .Should().Be(0, "la liste des clients se charge sans erreur contre le backend réel");

        // Assistant « Nouveau client » : étape profil rendue, validation de FORME exerçable.
        await createBtn.ClickAsync();
        var profil = Page.Locator("[data-testid='client-wizard-profil']");
        await profil.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        await Page.Locator("[data-testid='client-wizard-tenant-id']").FillAsync("ACME!!");
        await Page.Locator("[data-testid='client-wizard-profil-continue']").ClickAsync();

        var error = Page.Locator("[data-testid='client-wizard-profil-error']");
        await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await error.TextContentAsync()).Should().Contain("minuscules", "le message de validation est en français");
    }
}

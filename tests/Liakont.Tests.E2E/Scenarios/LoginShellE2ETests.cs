namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de preuve du harness (SOL05) : un utilisateur de rôle <c>lecture</c> se connecte via
/// le flux OIDC Keycloak complet (realm liakont-dev) et le shell applicatif Liakont s'affiche. Sera
/// enrichi par chaque item <c>blazor-page-item</c> (WEB*, SUP02, OPS03) héritant de
/// <see cref="KeycloakBaseE2ETest"/>.
/// </summary>
[Trait("Category", "E2E")]
public sealed class LoginShellE2ETests : KeycloakBaseE2ETest
{
    public LoginShellE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Login_via_keycloak_displays_liakont_shell()
    {
        // Flux OIDC complet : /login → Keycloak → /signin-oidc → accueil.
        await LoginViaKeycloakAsync();

        // Le shell connecté (layout ErpShellLayout) doit être visible.
        var shell = GetShellPage();
        await shell.WaitForShellAsync();
        (await shell.Shell.IsVisibleAsync()).Should().BeTrue(
            "le shell .erp-shell s'affiche pour un utilisateur authentifié (layout par défaut ErpShellLayout)");

        // L'accueil protégé "/" est désormais le tableau de bord (WEB01).
        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Tableau de bord" });
        (await heading.IsVisibleAsync()).Should().BeTrue(
            "la page d'accueil protégée affiche le tableau de bord après authentification");
    }
}

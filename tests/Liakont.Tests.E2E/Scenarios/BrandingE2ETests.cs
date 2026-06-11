namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E du branding d'instance (BRD01, marque grise) : après authentification OIDC, la barre latérale
/// affiche le NOM COMMERCIAL configuré de l'instance (défaut « Liakont ») et la marque socle « Stratum ERP »
/// n'apparaît NULLE PART (titre d'onglet ni coquille). Sur une instance white-label (CommercialName de
/// l'éditeur, PoweredByLiakont=false), ce même test prouverait l'absence de toute mention Liakont/socle.
/// Catégorie E2E : exécuté par tools/run-e2e.ps1 (harness SOL05), pas par la suite unit/intégration.
/// </summary>
[Trait("Category", "E2E")]
public sealed class BrandingE2ETests : KeycloakBaseE2ETest
{
    public BrandingE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Shell_displays_configured_brand_and_never_the_socle_name()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // La marque de la barre latérale = nom commercial de l'instance (défaut « Liakont »), jamais
        // la clé socle retirée « Stratum ERP ».
        var brand = Page.Locator(".erp-nav-brand");
        (await brand.TextContentAsync())?.Trim().Should().Be(
            "Liakont",
            "la barre latérale affiche le nom commercial de l'instance (BrandingOptions.CommercialName)");

        // Aucune fuite de la marque socle dans le titre d'onglet ni dans la coquille.
        (await Page.TitleAsync()).Should().NotContain("Stratum ERP");
        (await shell.Shell.TextContentAsync()).Should().NotContain("Stratum ERP");
    }
}

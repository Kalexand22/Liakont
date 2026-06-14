namespace Liakont.Tests.E2E.Scenarios;

using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// E2E de CLÔTURE du chantier realm unique (RLM04, ADR-0021 §6) : un utilisateur du <b>2e tenant</b>
/// (<c>tenant2</c>, company_id -002) se connecte EFFECTIVEMENT de bout en bout dans le <b>realm
/// partagé</b> <c>liakont-dev</c> et atteint le shell applicatif. Clôt la panne d'origine « un
/// utilisateur de tenant ne peut pas se connecter » (recette OPS03) et exerce RLM01→RLM03 ensemble :
/// <list type="bullet">
///   <item>RLM01 — l'utilisateur de tenant2 vit dans le realm partagé avec son attribut company_id
///         et son 2FA pré-enrôlé (login mot de passe + OTP) ;</item>
///   <item>RLM02 — la résolution du tenant courant est pilotée par le claim company_id (-002 → tenant2) ;</item>
///   <item>RLM03 — le cross-check global fail-closed LAISSE PASSER (company_id du jeton = company_id du
///         tenant résolu) au lieu de 403 ; sans le seed E2E d'outbox.tenants (RLM04), il bloquerait.</item>
/// </list>
/// La preuve d'ISOLATION (jeton A ⇏ tenant B) est portée par les tests d'intégration RLM03 ; ici on
/// prouve uniquement que le login d'un utilisateur de tenant dans le realm partagé ABOUTIT.
/// </summary>
[Trait("Category", "E2E")]
public sealed class TenantLoginSharedRealmE2ETests : KeycloakBaseE2ETest
{
    private const string Tenant2Username = "tenant2";

    public TenantLoginSharedRealmE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Tenant_user_logs_into_shared_realm_and_reaches_shell()
    {
        // Login OIDC complet de l'utilisateur du 2e tenant (mot de passe + 2FA) dans le realm partagé.
        await LoginViaKeycloakAsync(Tenant2Username);

        // Le cross-check (RLM03) a laissé passer : le shell connecté s'affiche (pas de 403/déconnexion).
        var shell = GetShellPage();
        await shell.WaitForShellAsync();
        (await shell.Shell.IsVisibleAsync()).Should().BeTrue(
            "un utilisateur du 2e tenant accède au shell après login dans le realm partagé (cross-check OK)");

        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Tableau de bord" });
        (await heading.IsVisibleAsync()).Should().BeTrue(
            "l'accueil protégé du 2e tenant s'affiche (company_id -002 résolu vers tenant2, accès servi)");
    }
}

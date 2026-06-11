namespace Liakont.Tests.E2E.Scenarios;

using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Tests.E2E;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// E2E (FIX209, décision E5) de l'assainissement de la navigation socle : dans la console rendue, l'« Annuaire »
/// (Agents/Équipes/Délégations), la « Sécurité » (Utilisateurs/Rôles), les « Règles de routage » et les
/// « Webhooks » des Notifications ne doivent PLUS apparaître ; seule « Templates » subsiste sous Notifications,
/// gardée par <c>liakont.settings</c>. Anti-faux-vert : les entrées retirées sont réellement ABSENTES du DOM, et
/// Templates est réellement VISIBLE pour le rôle qui porte settings et réellement ABSENT pour celui qui ne l'a pas.
/// </summary>
/// <remarks>
/// Catégorie E2E : exécuté par la suite Playwright (<c>tools/run-e2e.ps1</c>, conteneurs Keycloak + PostgreSQL),
/// pas par <c>run-tests</c>. Les preuves EXÉCUTÉES dans le pipeline (verify-fast + run-tests) sont portées par
/// les tests bUnit (<c>NotificationNavVisibilityFilterTests</c>, <c>ErpNavActiveStateTests</c>).
/// </remarks>
[Trait("Category", "E2E")]
public sealed class NavCleanupE2ETests : KeycloakBaseE2ETest
{
    private const string AnnuaireNavTestId = "nav-link-admin-agents";          // Annuaire › Agents (retiré)
    private const string SecuriteNavTestId = "nav-link-admin-identity-users";  // Sécurité › Utilisateurs (retiré)
    private const string RoutingRulesNavTestId = "nav-link-admin-notifications-routing"; // Règles de routage (retiré)
    private const string WebhooksNavTestId = "nav-link-admin-notifications-webhooks";     // Webhooks (retiré)
    private const string TemplatesNavTestId = "nav-link-admin-notifications-templates";   // Templates (conservé)
    private const string DocumentsNavTestId = "nav-link-documents";
    private const string AuditJournalNavTestId = "nav-link-admin-audit";          // Audit › Journal d'audit (masqué FIX303)
    private const string AuditPoliciesNavTestId = "nav-link-admin-audit-policies"; // Audit › Politiques (masqué FIX303)

    public NavCleanupE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Superviseur_Nav_Has_No_Annuaire_No_Securite_No_Routing_But_Keeps_Templates()
    {
        await LoginViaKeycloakAsync("superviseur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        (await Page.GetByTestId(AnnuaireNavTestId).CountAsync())
            .Should().Be(0, "l'« Annuaire » socle n'est plus câblé dans la nav Liakont (FIX209)");
        (await Page.GetByTestId(SecuriteNavTestId).CountAsync())
            .Should().Be(0, "la « Sécurité » socle n'est plus câblée dans la nav Liakont (FIX209)");
        (await Page.GetByTestId(RoutingRulesNavTestId).CountAsync())
            .Should().Be(0, "« Règles de routage » est supprimé (vestige services municipaux)");
        (await Page.GetByTestId(WebhooksNavTestId).CountAsync())
            .Should().Be(0, "« Webhooks » est hors périmètre Liakont et levait « No company context »");

        // Templates conservé et visible pour le superviseur (porte liakont.settings, matrice §3).
        (await Page.GetByTestId(TemplatesNavTestId).IsVisibleAsync())
            .Should().BeTrue("« Templates » est la seule entrée Notifications conservée et le superviseur porte liakont.settings");
    }

    [Fact]
    public async Task Operateur_Without_Settings_Does_Not_See_Templates_But_Sees_Documents()
    {
        await LoginViaKeycloakAsync("operateur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Les entrées non gardées restent visibles pour un rôle élevé non super-admin.
        (await Page.GetByTestId(DocumentsNavTestId).IsVisibleAsync())
            .Should().BeTrue("l'operateur voit les entrées de navigation non gardées (Documents)");

        // Templates est gardé par liakont.settings : l'operateur (read + actions) ne le voit pas.
        (await Page.GetByTestId(TemplatesNavTestId).CountAsync())
            .Should().Be(0, "l'operateur ne porte pas liakont.settings → l'entrée Templates est masquée");
    }

    [Fact]
    public async Task Superviseur_Nav_Has_No_Audit_Section()
    {
        // Le superviseur porte TOUTES les permissions Liakont (read/actions/settings/supervision) mais JAMAIS la
        // permission socle audit.trail.view (hors matrice §3) : la section « Audit » du socle reste masquée
        // (FIX303). Anti-faux-vert : les deux entrées socle sont réellement ABSENTES du DOM pour ce rôle élevé.
        await LoginViaKeycloakAsync("superviseur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        (await Page.GetByTestId(AuditJournalNavTestId).CountAsync())
            .Should().Be(0, "« Journal d'audit » exige audit.trail.view, jamais accordé à un rôle Liakont (FIX303)");
        (await Page.GetByTestId(AuditPoliciesNavTestId).CountAsync())
            .Should().Be(0, "« Politiques » d'audit exige audit.trail.view, jamais accordé à un rôle Liakont (FIX303)");
    }
}

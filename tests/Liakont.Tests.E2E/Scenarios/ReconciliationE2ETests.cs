namespace Liakont.Tests.E2E.Scenarios;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de la page Réconciliation des PDF (WEB08, F10) : prouve de bout en bout le GATING DE CAPACITÉ —
/// quand le tenant n'alimente aucun pool de PDF non rattachés, la section « Réconciliation » est ABSENTE de
/// la navigation et l'accès direct à l'URL affiche un état « non disponible » (jamais une page vide
/// trompeuse). C'est un critère d'acceptation de WEB08 (« page masquée si la capacité pool n'est pas
/// déclarée »), testable sans seeding et de façon déterministe (état par défaut du tenant E2E = pool vide).
/// <para>
/// Le rendu des trois files et le parcours d'action opérateur (confirmer / rejeter / lier) sont couverts par
/// les tests bUnit (ReconciliationViewTests : callbacks, aperçu PDF, sélecteur de lien) et d'intégration
/// (ReconciliationEndpointsIntegrationTests : file, confirm, reject, lien manuel, isolation tenant). Comme
/// pour WEB03b/WEB07a, le clic opérateur d'un élément permission-gated (<c>liakont.actions</c>) sous OIDC est
/// porté par le pont rôle→permission IDN01 (non encore livré) — l'E2E prouve ici la navigation et le gating.
/// </para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class ReconciliationE2ETests : KeycloakBaseE2ETest
{
    public ReconciliationE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    [Fact]
    public async Task Reconciliation_Section_Is_Hidden_And_Page_Reports_Unavailable_Without_A_Pdf_Pool()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // La navigation maître Liakont est rendue ; sans pool de PDF non rattachés, l'entrée Réconciliation
        // n'y figure pas (heuristique de capacité, WEB01/WEB08).
        await Page.Locator("[data-testid='nav-link-documents']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await Page.Locator("[data-testid='nav-link-reconciliation']").CountAsync())
            .Should().Be(0, "sans pool de PDF, la section Réconciliation est masquée dans la navigation");

        // Accès direct à l'URL : la page s'affiche mais signale explicitement son indisponibilité pour ce tenant.
        await Page.GotoAsync($"{BaseUrl}/reconciliation");

        var unavailable = Page.Locator("[data-testid='reconciliation-unavailable']");
        await unavailable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await unavailable.IsVisibleAsync())
            .Should().BeTrue("l'accès direct à /reconciliation sans capacité pool affiche l'état « non disponible »");
    }
}

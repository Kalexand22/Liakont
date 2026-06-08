namespace Liakont.Tests.E2E.Scenarios;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Tests.E2E;
using Microsoft.Playwright;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Test E2E de l'onglet Contrôles du détail document avec les actions garde-fou (WEB03b, F10 §2.3 /
/// F08 §A.4). Prouve le PARCOURS RÉEL de bout en bout : un utilisateur <c>lecture</c> se connecte (flux OIDC
/// Keycloak), ouvre la fiche d'un document BLOQUÉ seedé, et l'onglet Contrôles affiche le contrôle échoué —
/// SANS les boutons d'action (verdict / re-vérification), masqués faute de la permission <c>liakont.actions</c>
/// (la fiche reste consultable en lecture, conforme à WEB03a).
/// <para>
/// Le comportement des actions elles-mêmes (verdict B2C/B2B branché, re-vérification, message immédiat,
/// boutons visibles AVEC la permission) est couvert par les tests bUnit (DocumentDetailViewTests,
/// DocumentDetailTests, DocumentControlActionsServiceTests). Ici, comme pour WEB07a, l'E2E prouve la
/// navigation → rendu réel ; le clic opérateur d'un élément permission-gated sous OIDC est porté par WEB05
/// (qui dépend du pont rôle→permission IDN01, non encore livré).
/// </para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class DocumentControlsE2ETests : KeycloakBaseE2ETest
{
    // Identifiant fixe du document BLOQUÉ seedé : la fiche est ouverte directement sur cette URL.
    private static readonly Guid SeededDocId = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000b3");

    public DocumentControlsE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await SeedBlockedDocumentAsync();
    }

    [Fact]
    public async Task Lecture_user_sees_the_blocked_controls_tab_without_action_buttons()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        await Page.GotoAsync($"{BaseUrl}/documents/{SeededDocId}");

        // Le titre de la fiche s'affiche APRÈS le chargement asynchrone du détail (WaitForAsync vaut l'assertion).
        await Page.Locator("[data-testid='document-detail-title']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Onglet Contenu (sélectionné par défaut) : on attend son contenu pour laisser le circuit interactif
        // s'hydrater AVANT de cliquer un onglet (tant que l'hydratation n'est pas terminée, le clic est un no-op —
        // précédent DocumentDetailE2ETests).
        await Page.Locator("[data-testid='document-detail-number']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Onglet Contrôles : le contenu n'est rendu qu'une fois l'onglet sélectionné (clic via circuit interactif).
        await SelectTabAndWaitAsync("Contrôles", "document-detail-controls");

        // Le contrôle échoué est mis en évidence (document réellement bloqué).
        var blocked = Page.Locator("[data-testid='document-detail-controls-blocked']");
        await blocked.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await blocked.IsVisibleAsync()).Should().BeTrue("le document bloqué présente son contrôle échoué");

        // Lecture seule : aucune action (verdict / re-vérification) — la zone d'actions n'est pas rendue.
        (await Page.Locator("[data-testid='document-detail-controls-actions']").CountAsync())
            .Should().Be(0, "sans liakont.actions, les boutons garde-fou / re-vérification sont masqués (WEB03a en lecture)");
        (await Page.Locator("[data-testid='document-detail-recheck']").CountAsync())
            .Should().Be(0);
    }

    /// <summary>
    /// Sélectionne un onglet par son libellé et attend que son panneau soit rendu, avec retry d'hydratation
    /// (tant que le circuit Blazor n'est pas hydraté, le clic est un no-op) — jamais un sleep fixe optimiste.
    /// On attend d'abord que l'onglet soit visible, puis on retente le clic en absorbant TOUTE exception
    /// (le timeout d'attente du panneau peut être un <see cref="TimeoutException"/> .NET, pas un PlaywrightException).
    /// </summary>
    private async Task SelectTabAndWaitAsync(string tabName, string panelTestId)
    {
        var tab = Page.GetByRole(AriaRole.Tab, new() { Name = tabName });
        await tab.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        var panel = Page.Locator($"[data-testid='{panelTestId}']");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await tab.ClickAsync();
            try
            {
                await panel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
                return;
            }
            catch (Exception) when (attempt < 4)
            {
                // Circuit pas encore hydraté au moment du clic (le clic est un no-op) : on retente.
                await Page.WaitForTimeoutAsync(1_000);
            }
        }
    }

    /// <summary>
    /// Seede un document BLOQUÉ dans la base de test (tenant <c>default</c> = base système en E2E), avec
    /// l'indice « société » de la source. Idempotent (<c>ON CONFLICT DO NOTHING</c>) : la fixture de collection
    /// est partagée par tous les tests E2E.
    /// </summary>
    private async Task SeedBlockedDocumentAsync()
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents.documents
                (id, source_reference, document_number, document_type, issue_date,
                 supplier_siren, customer_name, customer_is_company_hint,
                 total_net, total_tax, total_gross, state, payload_hash)
            VALUES
                (@id, 'e2e/controls', 'E2E-CTRL-2026', 'invoice', DATE '2026-06-01',
                 '123456782', 'ACME SARL', TRUE,
                 1000, 162.80, 1162.80, 'Blocked', 'sha256:e2e-controls')
            ON CONFLICT (id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("id", SeededDocId);

        await command.ExecuteNonQueryAsync();
    }
}

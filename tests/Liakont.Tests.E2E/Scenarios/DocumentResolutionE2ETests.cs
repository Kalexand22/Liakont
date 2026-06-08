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
/// Test E2E des actions de RÉSOLUTION TERMINALE du détail document (WEB03c, F10 §2.3) : un utilisateur
/// <c>lecture</c> se connecte (flux OIDC Keycloak) et ouvre la fiche d'un document REJETÉ par la PA seedé.
/// Il voit le parcours « rejet » (onglet Contrôles : « Refusé par la Plateforme Agréée ») mais AUCUNE action
/// de résolution n'est offerte — les boutons sont masqués sans la permission <c>liakont.actions</c>
/// (acceptation « boutons d'action masqués sans la permission actions », prouvée de bout en bout en lecture).
/// <para>
/// Le PARCOURS D'ACTION lui-même (traitement manuel avec motif obligatoire, liaison au document de
/// remplacement, lien affiché dans l'historique) est couvert EXHAUSTIVEMENT par les tests bUnit
/// (<c>DocumentResolutionActionsTests</c>) et unitaires (<c>DocumentResolutionConsoleServiceTests</c>).
/// Il n'est pas rejouable end-to-end aujourd'hui car aucun utilisateur du realm E2E ne porte de claim
/// « permission » sous OIDC : la projection rôle realm → permission (ADR-0017 / item IDN01) n'est pas encore
/// en place, donc seul un super-admin verrait les boutons. C'est la MÊME limite que la console-web traite au
/// niveau de la gate (GATE_CONSOLE_WEB dépend de IDN01 + WEB05) ; ce test couvre, comme l'E2E de WEB07a, le
/// parcours réellement testable (navigation + rendu + masquage en lecture seule).
/// </para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class DocumentResolutionE2ETests : KeycloakBaseE2ETest
{
    // Identifiant fixe du document REJETÉ seedé : la fiche est ouverte directement sur cette URL.
    private static readonly Guid RejectedDocId = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000c3");

    public DocumentResolutionE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await SeedRejectedDocumentAsync();
    }

    [Fact]
    public async Task Read_only_user_opens_a_rejected_document_and_sees_no_resolution_actions()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Ouverture directe de la fiche du document rejeté seedé.
        await Page.GotoAsync($"{BaseUrl}/documents/{RejectedDocId}");

        await Page.Locator("[data-testid='document-detail-title']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Parcours « rejet » : l'onglet Contrôles montre le refus de la Plateforme Agréée.
        await SelectTabAndWaitAsync("Contrôles", "document-detail-controls");
        await Page.Locator("[data-testid='document-detail-controls-rejected']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Lecture seule : AUCUNE action de résolution n'est proposée (boutons masqués sans liakont.actions).
        (await Page.Locator("[data-testid='document-resolution-actions']").CountAsync())
            .Should().Be(0, "les actions de résolution sont masquées pour un utilisateur lecture (sans liakont.actions)");
    }

    /// <summary>
    /// Sélectionne un onglet par son libellé et attend que son panneau soit rendu, en retentant le clic tant
    /// que le circuit Blazor interactif n'est pas hydraté (absorption déterministe du délai d'hydratation —
    /// même helper que <c>DocumentDetailE2ETests</c>).
    /// </summary>
    private async Task SelectTabAndWaitAsync(string tabName, string panelTestId)
    {
        var tab = Page.GetByRole(AriaRole.Tab, new() { Name = tabName });
        var panel = Page.Locator($"[data-testid='{panelTestId}']");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await tab.ClickAsync();
            try
            {
                await panel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                return;
            }
            catch (PlaywrightException) when (attempt < 2)
            {
                await Page.WaitForTimeoutAsync(1_000);
            }
        }
    }

    /// <summary>
    /// Seede un document REJETÉ par la PA (tenant <c>default</c> = base système en E2E). Idempotent
    /// (<c>ON CONFLICT DO NOTHING</c>) : la fixture de collection est partagée par tous les tests E2E.
    /// </summary>
    private async Task SeedRejectedDocumentAsync()
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents.documents
                (id, source_reference, document_number, document_type, issue_date,
                 supplier_siren, customer_name, total_net, total_tax, total_gross, state, payload_hash)
            VALUES
                (@id, 'e2e/resolution', 'E2E-REJET-2026', 'invoice', DATE '2026-06-01',
                 '123456782', 'MARTIN SAS', 2800, 560.00, 3360.00, 'RejectedByPa', 'sha256:e2e-resolution')
            ON CONFLICT (id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("id", RejectedDocId);

        await command.ExecuteNonQueryAsync();
    }
}

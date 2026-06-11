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
/// Test E2E de la page détail document (WEB03a, F10 §2.3) : un utilisateur <c>lecture</c> se connecte (flux
/// OIDC Keycloak), ouvre la fiche d'un document seedé et parcourt les 4 onglets (Contenu / Contrôles /
/// Historique / Archive) jusqu'au bouton d'export pour contrôle fiscal. Le rendu détaillé par état (motif de
/// blocage, libellés d'événements, états vides) est couvert par les tests bUnit ; ici on prouve le parcours
/// réel de bout en bout. Un document est seedé dans la base de test (tenant <c>default</c> = base système en
/// E2E) avant la navigation, car la fiche détail exige un document existant.
/// </summary>
[Trait("Category", "E2E")]
public sealed class DocumentDetailE2ETests : KeycloakBaseE2ETest
{
    // Identifiant fixe du document seedé : la fiche est ouverte directement sur cette URL.
    private static readonly Guid SeededDocId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    public DocumentDetailE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await SeedDocumentAsync();
    }

    [Fact]
    public async Task Lecture_user_opens_a_document_detail_and_browses_the_four_tabs_then_export()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Ouverture directe de la fiche du document seedé (la navigation liste → détail est portée par l'action
        // de ligne « Voir » de WEB02 ; ici on prouve le rendu réel de la fiche de bout en bout).
        await Page.GotoAsync($"{BaseUrl}/documents/{SeededDocId}");

        // Le titre de la fiche s'affiche APRÈS le chargement asynchrone du détail (un WaitForAsync réussi
        // VAUT l'assertion de visibilité — il lève en cas de timeout).
        await Page.Locator("[data-testid='document-detail-title']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Onglet Contenu (sélectionné par défaut) : l'en-tête du document. On attend l'élément ASSERTÉ
        // lui-même (l'hydratation SSR→interactif re-rend le panneau ; WaitForAsync absorbe la transition).
        await Page.Locator("[data-testid='document-detail-number']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // FIX205 : la section « Détail des lignes » est rendue de bout en bout dans l'onglet Contenu (le détail
        // ligne à ligne — tableau quand le document est transmis, note sinon — n'est plus enfoui dans l'export).
        // Le rendu du TABLEAU lui-même (libellés, régime → catégorie/VATEX, cohérence des totaux) est couvert
        // exhaustivement et de façon déterministe par les tests bUnit + la projection canonique (round-trip réel).
        await Page.Locator("[data-testid='document-detail-lines-card']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Parcours des onglets Contrôles → Historique → Archive (le contenu d'un onglet n'est rendu que
        // lorsqu'il est sélectionné ; le clic dépend du circuit interactif → helper avec retry d'hydratation).
        await SelectTabAndWaitAsync("Contrôles", "document-detail-controls");
        await SelectTabAndWaitAsync("Historique", "document-detail-history");
        await SelectTabAndWaitAsync("Archive", "document-detail-archive");

        // L'export pour contrôle fiscal est offert et pointe vers l'endpoint d'export d'audit (téléchargement).
        var export = Page.Locator("[data-testid='document-detail-export']");
        await export.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await export.GetAttributeAsync("href"))
            .Should().Be($"/api/v1/documents/{SeededDocId}/audit-export", "l'export cible le dossier d'audit du document");
    }

    /// <summary>
    /// Sélectionne un onglet par son libellé et attend que son panneau soit rendu. Le clic dépend du circuit
    /// Blazor interactif : tant qu'il n'est pas hydraté, le clic est un no-op et le panneau ne s'affiche pas —
    /// on retente alors le clic après une courte attente (absorption déterministe du délai d'hydratation,
    /// jamais un sleep fixe optimiste).
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
                // Circuit pas encore hydraté au moment du clic : on retente.
                await Page.WaitForTimeoutAsync(1_000);
            }
        }
    }

    /// <summary>
    /// Seede un document minimal dans la base de test (tenant <c>default</c> = base système en E2E). Idempotent
    /// (<c>ON CONFLICT DO NOTHING</c>) : la fixture de collection est partagée par tous les tests E2E.
    /// </summary>
    private async Task SeedDocumentAsync()
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents.documents
                (id, source_reference, document_number, document_type, issue_date,
                 supplier_siren, customer_name, total_net, total_tax, total_gross, state, payload_hash)
            VALUES
                (@id, 'e2e/detail', 'E2E-DETAIL-2026', 'invoice', DATE '2026-06-01',
                 '123456782', 'DUPONT J.', 1000, 162.80, 1162.80, 'Issued', 'sha256:e2e-detail')
            ON CONFLICT (id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("id", SeededDocId);

        await command.ExecuteNonQueryAsync();
    }
}

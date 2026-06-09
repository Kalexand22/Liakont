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
/// Test E2E des actions d'envoi de la page Documents (WEB05, F10 §2.1). Prouve le PARCOURS RÉEL d'un
/// OPÉRATEUR sous OIDC réel : l'utilisateur de rôle <c>operateur</c> (= <c>lecture</c> + le claim
/// <c>liakont.actions</c> PROJETÉ depuis son rôle realm par le pont IDN01) se connecte, ouvre la page
/// Documents, VOIT les actions d'envoi (« Tout envoyer », « Lancer un traitement ») — masquées pour un
/// simple lecteur (DocumentsListE2ETests) — et DÉCLENCHE l'envoi groupé : la confirmation (récapitulatif +
/// mention irréversible) s'affiche, puis l'envoi déclenché rend un retour opérateur.
/// <para>
/// C'est l'E2E opérateur qui ferme le trou structurel ayant bloqué WEB05 : sans IDN01, aucun claim
/// <c>permission</c> n'était posé sous OIDC, donc les éléments permission-gated étaient masqués pour TOUS les
/// rôles E2E (aucun super-admin dans le realm). Le détail des actions (validation par document, codes d'audit,
/// refus métier) est couvert par les tests unitaires (DocumentSendActionsServiceTests) et bUnit (DocumentsTests).
/// </para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class DocumentSendActionsE2ETests : KeycloakBaseE2ETest
{
    // Identifiant fixe d'un document PRÊT À L'ENVOI seedé : « Tout envoyer » a alors au moins un document à émettre.
    private static readonly Guid SeededDocId = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000c5");

    public DocumentSendActionsE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await SeedReadyToSendDocumentAsync();
    }

    [Fact]
    public async Task Operator_sees_and_triggers_the_send_actions_on_the_documents_page()
    {
        // « operateur » = lecture + liakont.actions (projeté depuis le rôle realm par IDN01, sous OIDC réel).
        await LoginViaKeycloakAsync("operateur");

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        await Page.GotoAsync($"{BaseUrl}/documents");

        // L'opérateur VOIT les actions d'envoi : la projection rôle→permission d'IDN01 pose le claim
        // liakont.actions (sans lui, les boutons seraient masqués — cf. DocumentsListE2ETests pour « lecture »).
        var sendAll = Page.Locator("[data-testid='documents-send-all']");
        await sendAll.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await sendAll.IsVisibleAsync()).Should().BeTrue("l'opérateur (liakont.actions) voit « Tout envoyer »");
        (await Page.Locator("[data-testid='documents-trigger-run']").IsVisibleAsync())
            .Should().BeTrue("l'opérateur voit « Lancer un traitement »");

        // DÉCLENCHE « Tout envoyer » : la confirmation (récapitulatif + mention irréversible) s'affiche après
        // hydratation du circuit (retry du clic tant que le circuit interactif n'est pas hydraté — jamais un sleep).
        await ClickAndWaitAsync("documents-send-all", "documents-send-all-confirm");
        (await Page.Locator("[data-testid='documents-send-all-confirm-text']").TextContentAsync())
            .Should().Contain("IRRÉVERSIBLE", "la confirmation prévient que l'envoi est irréversible (F10)");

        // Confirme : l'envoi groupé du tenant est déclenché et un retour opérateur s'affiche (le circuit est
        // désormais hydraté — le clic précédent a abouti —, un simple clic suffit ici).
        var feedback = Page.Locator("[data-testid='documents-send-feedback']");
        await Page.Locator("[data-testid='documents-send-all-confirm-button']").ClickAsync();
        await feedback.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await feedback.IsVisibleAsync()).Should().BeTrue("l'envoi déclenché affiche un retour opérateur");
    }

    // NB : le câblage page → service → retour de la barre d'actions groupées « Envoyer la sélection » (et le
    // StateHasChanged() explicite qu'il exige) est couvert de façon DÉTERMINISTE par un test bUnit (DocumentsTests :
    // « Envoyer_La_Selection_Bulk_Action_Calls_The_Service_And_Shows_Feedback »), qui invoque directement le rappel
    // Execute de la BulkActionConfig — la sélection RÉELLE d'une ligne de la grille Radzen dépend d'un actionnement
    // (visible/enabled/stable) instable en E2E (re-rendu du circuit), ce qui rendrait ce test E2E intermittent.

    /// <summary>
    /// Clique un bouton (par data-testid) et attend qu'un élément attendu apparaisse, avec retry d'hydratation
    /// (tant que le circuit Blazor n'est pas hydraté, le clic est un no-op) — jamais un sleep fixe optimiste.
    /// On absorbe TOUTE exception sur l'attente (le timeout peut être un <see cref="TimeoutException"/> .NET).
    /// </summary>
    private async Task ClickAndWaitAsync(string buttonTestId, string expectedTestId)
    {
        var button = Page.Locator($"[data-testid='{buttonTestId}']");
        await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        var expected = Page.Locator($"[data-testid='{expectedTestId}']");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await button.ClickAsync();
            try
            {
                await expected.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
                return;
            }
            catch (Exception) when (attempt < 4)
            {
                await Page.WaitForTimeoutAsync(1_000);
            }
        }
    }

    /// <summary>
    /// Seede un document PRÊT À L'ENVOI (<c>ReadyToSend</c>) dans la base de test (tenant <c>default</c> = base
    /// système en E2E). Idempotent (<c>ON CONFLICT DO NOTHING</c>) : la fixture de collection est partagée.
    /// </summary>
    private async Task SeedReadyToSendDocumentAsync()
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
                (@id, 'e2e/send', 'E2E-SEND-2026', 'invoice', DATE '2026-06-01',
                 '123456782', 'ACME SARL', FALSE,
                 1000, 162.80, 1162.80, 'ReadyToSend', 'sha256:e2e-send')
            ON CONFLICT (id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("id", SeededDocId);

        await command.ExecuteNonQueryAsync();
    }
}

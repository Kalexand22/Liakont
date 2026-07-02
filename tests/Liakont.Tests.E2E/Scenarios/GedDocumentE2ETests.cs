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
/// Test E2E de la fiche document GED (/ged/document/{id}, GED09b, F19 §6.7) : un utilisateur <c>lecture</c> (rôle
/// portant <c>liakont.ged.read</c> mais PAS <c>liakont.ged.confidential</c>) se connecte (OIDC Keycloak), ouvre la
/// fiche d'un document GED seedé et voit ses méta + son axe PUBLIC ; l'axe CONFIDENTIEL est MASQUÉ server-side
/// (jamais présent dans la page — anti-oracle). Le rendu détaillé par état est couvert par les tests bUnit ; ici
/// on prouve le parcours réel de bout en bout, masquage compris. Un document + ses axes sont seedés dans la base
/// de test (tenant <c>default</c> = base système en E2E) avant la navigation.
/// </summary>
[Trait("Category", "E2E")]
public sealed class GedDocumentE2ETests : KeycloakBaseE2ETest
{
    private static readonly Guid SeededDocId = Guid.Parse("aaaaaaaa-0ed0-4000-8000-000000000001");
    private static readonly Guid PublicAxisId = Guid.Parse("aaaaaaaa-0ed0-4000-8000-000000000002");
    private static readonly Guid SecretAxisId = Guid.Parse("aaaaaaaa-0ed0-4000-8000-000000000003");
    private static readonly Guid PublicLinkId = Guid.Parse("aaaaaaaa-0ed0-4000-8000-000000000004");
    private static readonly Guid SecretLinkId = Guid.Parse("aaaaaaaa-0ed0-4000-8000-000000000005");

    public GedDocumentE2ETests(
        KeycloakE2EWebFactory factory,
        PlaywrightFixture playwright,
        ITestOutputHelper output)
        : base(factory, playwright, output)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await SeedGedDocumentAsync();
    }

    [Fact]
    public async Task Lecture_user_opens_a_ged_document_and_confidential_axis_is_masked()
    {
        await LoginViaKeycloakAsync();

        var shell = GetShellPage();
        await shell.WaitForShellAsync();

        // Ouverture directe de la fiche du document GED seedé (la navigation recherche → fiche est portée par
        // la page /ged/recherche, GED09a ; ici on prouve le rendu réel de la fiche de bout en bout).
        await Page.GotoAsync($"{BaseUrl}/ged/document/{SeededDocId}");

        // Le titre de la fiche s'affiche APRÈS le chargement asynchrone (un WaitForAsync réussi VAUT l'assertion).
        await Page.Locator("[data-testid='ged-document-page-title']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // Statut indexé + section intégrité rendus.
        await Page.Locator("[data-testid='ged-document-status']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await Page.Locator("[data-testid='ged-document-status']").TextContentAsync())
            .Should().Contain("Indexé");
        await Page.Locator("[data-testid='ged-document-integrity']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        // L'axe PUBLIC est visible ; l'axe CONFIDENTIEL est masqué server-side (absent de la page).
        var axes = Page.Locator("[data-testid='ged-document-axes']");
        await axes.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        (await axes.TextContentAsync()).Should().Contain("LOT-E2E");

        var body = await Page.Locator("body").TextContentAsync();
        body.Should().NotContain("SECRET-E2E", "la valeur d'un axe confidentiel n'est jamais restituée sans le droit (anti-oracle)");
        body.Should().NotContain("Montant secret", "le libellé d'un axe confidentiel n'est jamais restitué sans le droit");
    }

    /// <summary>
    /// Seede un document GED minimal + un axe PUBLIC (visible) et un axe CONFIDENTIEL (masqué sans le droit) dans
    /// la base de test (tenant <c>default</c> = base système en E2E). Idempotent (<c>ON CONFLICT DO NOTHING</c>) :
    /// la fixture de collection est partagée par tous les tests E2E.
    /// </summary>
    private async Task SeedGedDocumentAsync()
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ged_index.managed_documents (id, title, doc_kind, status)
            VALUES (@doc, 'Bordereau acheteur E2E', 'bordereau', 'indexed')
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO ged_catalog.axis_definitions (id, code, label, data_type, is_searchable, is_confidential, is_active)
            VALUES
                (@pubAxis, 'numero_lot_e2e', 'Numéro de lot', 'string', true, false, true),
                (@secretAxis, 'montant_secret_e2e', 'Montant secret', 'string', true, true, true)
            ON CONFLICT (code) DO NOTHING;

            INSERT INTO ged_index.document_axis_links (id, managed_document_id, axis_id, value_string, normalized_value, source)
            VALUES
                (@pubLink, @doc, @pubAxis, 'LOT-E2E', 'lot-e2e', 'manual'),
                (@secretLink, @doc, @secretAxis, 'SECRET-E2E', 'secret-e2e', 'manual')
            ON CONFLICT (id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("doc", SeededDocId);
        command.Parameters.AddWithValue("pubAxis", PublicAxisId);
        command.Parameters.AddWithValue("secretAxis", SecretAxisId);
        command.Parameters.AddWithValue("pubLink", PublicLinkId);
        command.Parameters.AddWithValue("secretLink", SecretLinkId);

        await command.ExecuteNonQueryAsync();
    }
}

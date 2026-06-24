namespace Liakont.Modules.Pipeline.Tests.Integration.B2cReporting;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Pipeline.Tests.Integration.Send;
using Xunit;

/// <summary>
/// B4 — JOB e-reporting B2C de la MARGE (flux 10.3, enchères) sur base réelle (Testcontainers) : découverte des
/// déclarations de marge prêtes → résolution du taux (mapping F03, part FRAIS) → agrégation jour×devise×taux →
/// transmission PA (TMA1/SE) → journal d'émission APPEND-ONLY (anti-doublon attempt-once par document, décision
/// D3) → gel du lien reporting↔pièce (clé document, décision D2). Couvre aussi la garde D1 (une déclaration de
/// marge ne part JAMAIS par la voie document de SendTenantJob) et le fail-closed (taux non mappé → bloqué).
/// Base ISOLÉE par méthode.
/// </summary>
public sealed class B2cMarginAggregatorJobTests : IAsyncLifetime
{
    private const string SendB2cTransaction = "SendB2cTransactionAsync";
    private const string SendDocument = "SendDocumentAsync";

    // Détail journalisé par le plug-in factice pour la transaction agrégée (catégorie/rôle/jour de la fixture).
    private const string MarginTxDetail = "Tma1/Seller/20260120";

    private readonly PipelineSendHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public Task DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Margin_Declaration_Is_Aggregated_Transmitted_And_Emission_Is_Journaled()
    {
        // PA publiée déclarant le report du montant de marge (défaut V1) : la déclaration de marge est agrégée
        // (jour×devise×taux) et transmise en TMA1/SE, et l'émission est journalisée (Pending → Issued + id PA).
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var sourceReference = "ba-" + documentId.ToString("N");
        var declaration = CheckIntegrationFixtures.BuildB2cMarginDeclaration(sourceReference, "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cMarginAsync();

        _harness.PaCallCount(SendB2cTransaction, MarginTxDetail)
            .Should().Be(1, "la marge du jour est transmise en UNE transaction agrégée (TMA1/SE).");

        // Attempt-once crash-safe : Pending écrit AVANT le POST, puis Issued après confirmation (exactement ces 2).
        var emissions = await _harness.GetB2cMarginEmissionsAsync(documentId);
        emissions.Select(e => e.Status).Should().Equal("Pending", "Issued");
        emissions[^1].PaEmissionId.Should().NotBeNullOrWhiteSpace("l'id serveur de la transaction est journalisé à l'émission.");

        // Le job NE transitionne PAS la machine à états du document (projection parallèle, comme l'agrégation paiement).
        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("ReadyToSend");
    }

    [Fact]
    public async Task Issued_Margin_Aggregate_Freezes_Reversible_Reporting_Piece_Link()
    {
        // D2/B6 : APRÈS confirmation d'envoi, le lien reporting↔pièce est gelé au grain DOCUMENT — l'export fiscal
        // (GetByDocumentAsync) le retrouve (réversibilité N→1 préservée, pas de re-clé sur l'agrégat).
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var sourceReference = "ba-" + documentId.ToString("N");
        var declaration = CheckIntegrationFixtures.BuildB2cMarginDeclaration(sourceReference, "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cMarginAsync();

        var links = await _harness.GetReportingPieceLinksAsync(documentId);
        links.Should().ContainSingle("un agrégat de marge transmis gèle exactement un lien vers la pièce source.");
        links.Single().SourceReference.Should().Be(sourceReference);
        links.Single().DocumentId.Should().Be(documentId);
    }

    [Fact]
    public async Task Rerun_Does_Not_Re_Emit_An_Already_Issued_Document()
    {
        // D3 (attempt-once) : un document déjà tenté est EXCLU du run suivant — JAMAIS un 2e POST (l'API SuperPDP
        // n'a aucune clé d'idempotence ; 2 POST = 2 lignes).
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildB2cMarginDeclaration("ba-" + documentId.ToString("N"), "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cMarginAsync();
        await _harness.RunB2cMarginAsync();

        _harness.PaCallCount(SendB2cTransaction, MarginTxDetail)
            .Should().Be(1, "le 2e run exclut le document déjà tenté — jamais 2 POST.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Count(e => e.Status == "Issued")
            .Should().Be(1, "une seule émission Issued, même après ré-exécution.");
    }

    [Fact]
    public async Task Send_Job_Holds_Margin_Declaration_From_The_Per_Document_Path()
    {
        // D1 : une déclaration de MARGE (honoraires, sans TVA distincte) est transmise EXCLUSIVEMENT par le job
        // agrégé ; SendTenantJob la TIENT hors de la voie document (SendDocumentAsync la rejetterait — pas de
        // destinataire SIREN). Anti-régression de la garde existante (capacité B2C) : la marge est déférée AVANT.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildB2cMarginDeclaration("ba-" + documentId.ToString("N"), "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should()
            .Be("ReadyToSend", "une déclaration de marge reste ReadyToSend — différée vers le job agrégé.");
        _harness.PaCallCount(SendDocument, declaration.Number).Should()
            .Be(0, "la déclaration de marge ne part JAMAIS par la voie document (jamais SendDocumentAsync).");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("SendTenantJob ne touche pas le journal d'émission B2C (c'est l'affaire du job agrégé).");
    }

    [Fact]
    public async Task Margin_With_Unmapped_Fee_Rate_Is_Blocked_And_Never_Transmitted()
    {
        // Fail-closed (CLAUDE.md n°2/3) : un honoraire à code régime ABSENT de la table validée → taux non
        // résolu → marge BLOQUÉE (UnmappedRate) → aucune transmission, aucune entrée d'émission. Jamais deviné.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildB2cMarginDeclaration("ba-" + documentId.ToString("N"), "REGIME_ABSENT");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cMarginAsync();

        _harness.PaCallCount(SendB2cTransaction, MarginTxDetail).Should()
            .Be(0, "un honoraire à taux non mappé bloque la marge — jamais transmise.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("un document bloqué n'écrit aucune entrée d'émission (jamais Pending sur du non-transmis).");
    }
}

namespace Liakont.Modules.Pipeline.Tests.Integration.B2cReporting;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Contracts;
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
    public async Task Margin_Emission_Aggregates_Are_Read_Grouped_By_Emission_Batch_With_Current_Status()
    {
        // Vue console (B4) : le journal append-only (Pending puis Issued PAR DOCUMENT) est lu REGROUPÉ par lot
        // d'émission (une TRANSMISSION = un POST) — une ligne par transmission, état COURANT, nb de pièces.
        // Exerce le SQL réel (CTE + fenêtre ROW_NUMBER + COUNT DISTINCT) contre le conteneur.
        await _harness.UsePublishedFakeAsync();

        // Deux documents de marge du MÊME jour, dans le MÊME run → UN seul agrégat transmis (un POST, un lot).
        foreach (var i in new[] { 1, 2 })
        {
            var documentId = Guid.NewGuid();
            var declaration = CheckIntegrationFixtures.BuildB2cMarginDeclaration("ba-agg-" + i + "-" + documentId.ToString("N"), "NORMAL");
            await _harness.SeedDetectedAndStageAsync(documentId, declaration);
            await _harness.MarkReadyToSendAsync(documentId);
        }

        await _harness.RunB2cMarginAsync();

        var aggregates = await _harness.ReadMarginEmissionAggregatesAsync();
        aggregates.Should().ContainSingle("les 2 documents émis dans le même POST forment UNE transmission (un lot d'émission).");
        var aggregate = aggregates[0];
        aggregate.Status.Should().Be("Issued", "l'état COURANT (dernière entrée) prime sur le Pending initial.");
        aggregate.DocumentCount.Should().Be(2, "deux pièces ont contribué à cette transmission.");
        aggregate.PaEmissionId.Should().NotBeNullOrWhiteSpace("l'id serveur est exposé pour une transmission émise.");
        aggregate.Category.Should().Be("TMA1");
        aggregate.Role.Should().Be("SE");

        // Filtre de DATE pur (année-mois sur le jour de l'agrégat) : le mois de l'agrégat le retourne, un autre non.
        (await _harness.ReadMarginEmissionAggregatesAsync("2026-01")).Should().ContainSingle();
        (await _harness.ReadMarginEmissionAggregatesAsync("2025-12")).Should().BeEmpty();
    }

    [Fact]
    public async Task Two_Transmissions_Of_Identical_Content_Are_Two_Rows_Never_Collapsed()
    {
        // Régression (review P2) : deux transmissions RÉELLES distinctes d'un MÊME contenu (même jour, mêmes
        // montants — barème d'enchères standard) partagent le content_hash. Un document tardif sur un jour déjà
        // émis part dans un NOUVEL agrégat (POST séparé, lot distinct). La vue console doit montrer DEUX lignes
        // (deux transmissions = deux POST), jamais une seule (compte gonflé + un id PA masqué). Le regroupement
        // par lot d'émission le garantit (regrouper par content_hash les fusionnerait).
        await _harness.UsePublishedFakeAsync();

        // 1re transmission.
        var doc1 = Guid.NewGuid();
        await _harness.SeedDetectedAndStageAsync(doc1, CheckIntegrationFixtures.BuildB2cMarginDeclaration("ba-id1-" + doc1.ToString("N"), "NORMAL"));
        await _harness.MarkReadyToSendAsync(doc1);
        await _harness.RunB2cMarginAsync();

        // 2e document IDENTIQUE (même jour, mêmes montants) arrivé APRÈS → nouvelle transmission (POST séparé),
        // même content_hash mais lot d'émission distinct.
        var doc2 = Guid.NewGuid();
        await _harness.SeedDetectedAndStageAsync(doc2, CheckIntegrationFixtures.BuildB2cMarginDeclaration("ba-id2-" + doc2.ToString("N"), "NORMAL"));
        await _harness.MarkReadyToSendAsync(doc2);
        await _harness.RunB2cMarginAsync();

        var aggregates = await _harness.ReadMarginEmissionAggregatesAsync();
        aggregates.Should().HaveCount(2, "deux transmissions distinctes (POST séparés) ne sont jamais fusionnées, même à contenu identique.");
        aggregates.Should().OnlyContain(a => a.Status == "Issued");
        aggregates.Should().OnlyContain(a => a.DocumentCount == 1, "chaque transmission ne porte qu'UNE pièce — aucun gonflement du compte.");
        aggregates.Select(a => a.EmissionBatchId).Distinct().Should().HaveCount(2, "chaque transmission a son propre lot d'émission.");
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

    [Fact]
    public async Task Taxable_Auction_With_Fees_Is_Not_Marked_Nor_Aggregated()
    {
        // Discrimination sourcée (F03 §3) : un bordereau d'enchères TAXABLE RÉALISTE (adjudication S 20 %, TVA
        // distincte > 0) porteur de frais N'EST PAS une déclaration de marge — la plateforme ne le marque pas
        // (le régime de la marge vient du mapping VALIDÉ, jamais d'un « TVA = 0 » deviné). Il atteint ReadyToSend
        // (TVA > 0 ⇒ hors garde « marge non classée ») et le job B4 l'IGNORE (catégorie S ≠ E).
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildTaxableAuctionWithFees("ba-" + documentId.ToString("N"));
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cMarginAsync();

        _harness.PaCallCount(SendB2cTransaction, MarginTxDetail).Should()
            .Be(0, "une adjudication taxable (S, TVA distincte) n'est pas un régime de marge → document non marqué, jamais agrégé.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("un document non marqué n'écrit aucune entrée d'émission B2C marge.");
    }

    [Fact]
    public async Task Margin_Fees_But_Adjudication_Not_Mappable_Is_Traced_Not_Silently_Skipped()
    {
        // Traçabilité (review P2) : un bordereau porteur de frais dont l'adjudication n'est plus mappable depuis
        // le CHECK (régime décroché de la table validée) n'est PAS agrégé MAIS est TRACÉ dans le journal du run
        // B4 (jamais un skip muet), miroir du HOLD TvaUnresolved de SendTenantJob. Le document reste ReadyToSend.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var declaration = CheckIntegrationFixtures.BuildB2cMarginDeclaration(
            "ba-" + documentId.ToString("N"), "NORMAL", adjudicationRegimeCode: "REGIME_ABSENT");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cMarginAsync();

        _harness.PaCallCount(SendB2cTransaction, MarginTxDetail).Should()
            .Be(0, "une adjudication non mappable → document jamais agrégé.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("aucune émission sur un document non agrégé.");

        var runs = await _harness.GetRunsAsync();
        var run = runs.First(r => r.RunType == PipelineRunType.B2cMarginAggregate);
        run.Detail.Should().Contain(
            "AdjudicationNotMapped",
            "le document dégradé est TRACÉ dans le journal du run B4 (jamais un skip muet) — miroir du HOLD SEND.");
        (await _harness.GetDocumentStateAsync(documentId)).Should()
            .Be("ReadyToSend", "le document reste prêt à l'envoi — repris quand la table de mapping est rétablie.");
    }

    [Fact]
    public async Task Document_With_Margin_Fees_But_Professional_Buyer_Is_Not_Marked_Nor_Aggregated()
    {
        // B2B (acheteur identifié par un SIREN) : e-invoicing B2B, JAMAIS un e-reporting B2C de la marge
        // (F03 §2.4). La plateforme ne marque pas → B4 l'ignore. Anti-régression du scénario historique (le B2B
        // cassé par le lot B2C, cf. mémoire) : un acheteur SIREN à une vente marge ne file pas dans l'agrégat.
        await _harness.UsePublishedFakeAsync();

        var documentId = Guid.NewGuid();
        var buyer = new PivotPartyDto("Galerie Pro SARL", siren: "945678902");
        var declaration = CheckIntegrationFixtures.BuildB2cMarginDeclaration(
            "ba-" + documentId.ToString("N"), "NORMAL", customer: buyer);
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cMarginAsync();

        _harness.PaCallCount(SendB2cTransaction, MarginTxDetail).Should()
            .Be(0, "un acheteur professionnel (SIREN) relève de l'e-invoicing B2B, jamais de l'e-reporting B2C marge.");
        (await _harness.GetB2cMarginEmissionsAsync(documentId)).Should()
            .BeEmpty("un document B2B n'écrit aucune entrée d'émission B2C marge.");
    }
}

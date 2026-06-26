namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Tests SMOKE du mode FIXTURES (<see cref="EncheresV6FixtureExtractor"/>) sur le modèle BA/BV : un
/// snapshot JSON (bordereaux acheteur + vendeur) rejoue exactement la transformation du mode ODBC —
/// le BA porte la commission acheteur (BuyerFees), le BV la commission vendeur (SellerFees). Couverture
/// fiscale détaillée dans <see cref="EncheresV6RowMapperTests"/>. NB : suite fixtures exhaustive
/// (avoirs lettrés, paiements, régimes, fusion de répertoire) à reconstruire sur le modèle BA/BV.
/// </summary>
public class EncheresV6FixtureExtractorTests
{
    private const string Snapshot = @"{
      ""regimes"": [],
      ""bordereaux"": [
        { ""no_ba"": ""100022"", ""bordereau_ou_avoir"": ""B"", ""date_vente"": ""2024-01-12"",
          ""nom"": ""Acheteur Particulier (fictif)"", ""code_postal"": ""35000"", ""ville"": ""Rennes"", ""code_pays"": ""FR"",
          ""total_bordereau"": 2401.28,
          ""lignes"": [
            { ""type_ligne"": ""1"", ""no_ligne_pv"": ""1"", ""libelle_ligne"": ""Adjudication lot 1"",
              ""montant_adj_ht"": 2000.0, ""montant_frais_ht"": 334.40, ""montant_tva_frais"": 66.88, ""code_regime"": ""6"" }
          ] }
      ],
      ""bordereaux_vendeur"": [
        { ""no_bv"": ""100012"", ""bordereau_ou_avoir"": ""B"", ""date_vente"": ""2024-01-12"",
          ""nom"": ""Vendeur Commettant (fictif)"", ""code_regime_tva"": ""6"", ""total_bordereau"": 1860.0,
          ""lignes"": [
            { ""type_ligne"": ""1"", ""no_ligne_pv"": ""1"", ""montant_adj_ht"": 2000.0 },
            { ""type_ligne"": ""2"", ""no_ligne_pv"": ""0"", ""libelle_ligne"": ""Frais de vente 15%"", ""mtt_frais_ht"": 300.0, ""mtt_tva_frais"": 60.0 }
          ] }
      ]
    }";

    [Fact]
    public void ExtractDocuments_emits_buyer_and_seller_bordereaux()
    {
        EncheresV6FixtureExtractor extractor = EncheresV6FixtureExtractor.FromJson(Snapshot);

        List<PivotDocumentDto> docs = extractor.ExtractDocuments(new DateTime(2024, 1, 1), new DateTime(2025, 1, 1)).ToList();

        docs.Should().HaveCount(2);

        PivotDocumentDto ba = docs.Single(d => d.SourceReference.StartsWith("encheresv6:ba:", StringComparison.Ordinal));
        ba.BuyerFees.Should().ContainSingle();
        ba.BuyerFees![0].NetAmount.Should().Be(401.28m, "commission acheteur TTC");

        PivotDocumentDto bv = docs.Single(d => d.SourceReference.StartsWith("encheresv6:bv:", StringComparison.Ordinal));
        bv.SellerFees.Should().ContainSingle();
        bv.SellerFees![0].NetAmount.Should().Be(360.00m, "commission vendeur TTC (type 2), débours exclus");
    }

    [Fact]
    public void ExtractDocuments_emits_facture_client_as_plain_b2c_document()
    {
        // Une facture client (document ORDINAIRE hors enchères) rejouée par les fixtures : lignes plates au prix
        // total, AUCUN frais d'enchères, code_tva en clé de régime — parité stricte avec le mode ODBC (F03 §2.9).
        const string snapshot = @"{
          ""regimes"": [], ""bordereaux"": [], ""bordereaux_vendeur"": [],
          ""factures_clients"": [
            { ""no_fact"": ""00100007"", ""facture_ou_avoir"": ""F"", ""date_fact"": ""2024-04-12"",
              ""nom"": ""LOBRY"", ""prenom"": ""STEEVE"", ""adresse1"": ""15 rue Boberie"", ""cp"": ""53000"", ""ville"": ""LAVAL"", ""code_pays"": ""FR"",
              ""montant_ht"": 144.0, ""montant_tva"": 28.8, ""montant_ttc"": 172.8, ""code_devise"": ""EURO"",
              ""lignes"": [
                { ""type_ligne"": ""1"", ""no_ligne"": ""1"", ""code_article"": ""CV"", ""designation"": ""Caisse de Vins"", ""qte"": 12, ""prix_unitaire_ht"": 12.0, ""code_tva"": 1, ""taux_tva"": 20.0 },
                { ""type_ligne"": ""2"", ""no_ligne"": ""1"", ""code_article"": """", ""designation"": ""Carte bancaire"", ""qte"": 0, ""prix_unitaire_ht"": 172.8, ""code_tva"": 0, ""taux_tva"": 0.0 }
              ] }
          ] }";

        EncheresV6FixtureExtractor extractor = EncheresV6FixtureExtractor.FromJson(snapshot);

        List<PivotDocumentDto> docs = extractor.ExtractDocuments(new DateTime(2024, 1, 1), new DateTime(2025, 1, 1)).ToList();

        PivotDocumentDto fc = docs.Single(d => d.SourceReference.StartsWith("encheresv6:fc:", StringComparison.Ordinal));
        fc.Number.Should().Be("00100007");
        fc.BuyerFees.Should().BeNull("une facture ordinaire ne porte aucun frais d'enchères");
        fc.SellerFees.Should().BeNull();
        fc.OperationCategory.Should().BeNull("la nature est plateforme (profil tenant)");
        fc.Lines.Should().ContainSingle("le règlement (type 2) est écarté");
        fc.Lines[0].NetAmount.Should().Be(144.00m);
        fc.Lines[0].Taxes[0].TaxAmount.Should().Be(28.80m, "TVA ligne = HT × taux_tva source");
        fc.Lines[0].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("20", "clé = taux effectif (taux_tva)");
        fc.Totals.SourceTotalGross.Should().Be(172.80m);
    }

    [Fact]
    public void ExtractDocuments_emits_note_hono_as_plain_b2c_service_document()
    {
        // Une note d'honoraires d'inventaire (prestation de services) rejouée par les fixtures : honoraires (type 1)
        // au prix total, TVA distincte source, taux effectif recouvré en clé de régime, règlement (type 3) exclu.
        const string snapshot = @"{
          ""regimes"": [], ""bordereaux"": [], ""bordereaux_vendeur"": [], ""factures_clients"": [],
          ""notes_hono"": [
            { ""no_note_hono"": ""100008"", ""facture_ou_avoir"": ""F"", ""date_facture"": ""2024-04-12"",
              ""nom"": ""GLOUX"", ""adresse"": ""1 rue de la Criée"", ""code_postal"": ""56000"", ""ville"": ""Vannes"", ""code_pays"": ""FR"",
              ""montant_ttc"": 27.6, ""code_devise"": ""EURO"",
              ""lignes"": [
                { ""type_ligne"": ""1"", ""libelle"": ""Honoraires d'inventaire"", ""montant_ht"": 23.0, ""montant_tva"": 4.6 },
                { ""type_ligne"": ""3"", ""code_ligne"": ""CE"", ""libelle"": ""Chèque"", ""montant_ht"": 27.6, ""montant_tva"": 0.0 }
              ] }
          ] }";

        EncheresV6FixtureExtractor extractor = EncheresV6FixtureExtractor.FromJson(snapshot);

        List<PivotDocumentDto> docs = extractor.ExtractDocuments(new DateTime(2024, 1, 1), new DateTime(2025, 1, 1)).ToList();

        PivotDocumentDto nh = docs.Single(d => d.SourceReference.StartsWith("encheresv6:nh:", StringComparison.Ordinal));
        nh.Number.Should().Be("100008");
        nh.BuyerFees.Should().BeNull("une note d'honoraires ne porte aucun frais d'enchères");
        nh.SellerFees.Should().BeNull();
        nh.OperationCategory.Should().BeNull("la nature (TPS1) est plateforme (profil tenant)");
        nh.Lines.Should().ContainSingle("le règlement (type 3) est exclu");
        nh.Lines[0].NetAmount.Should().Be(23.00m);
        nh.Lines[0].Taxes[0].TaxAmount.Should().Be(4.60m, "TVA distincte source (montant_tva)");
        nh.Lines[0].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("20", "taux effectif recouvré (4,6/23)");
        nh.Totals.SourceTotalGross.Should().Be(27.60m);
    }

    [Fact]
    public void FromJson_rejects_duplicate_no_note_hono_for_idempotence()
    {
        // Deux notes de même no_note_hono produiraient deux pivots de même SourceReference (double-déclaration, R2).
        const string dup = @"{ ""regimes"": [], ""bordereaux"": [], ""bordereaux_vendeur"": [], ""factures_clients"": [], ""notes_hono"": [
            { ""no_note_hono"": ""100008"", ""facture_ou_avoir"": ""F"", ""date_facture"": ""2024-04-12"", ""nom"": ""A"", ""lignes"": [] },
            { ""no_note_hono"": ""100008"", ""facture_ou_avoir"": ""F"", ""date_facture"": ""2024-04-13"", ""nom"": ""B"", ""lignes"": [] }
          ] }";

        ((Action)(() => EncheresV6FixtureExtractor.FromJson(dup))).Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void FromJson_rejects_duplicate_no_fact_for_idempotence()
    {
        // Deux factures de même no_fact produiraient deux pivots de même SourceReference (double-déclaration, R2).
        const string dup = @"{ ""regimes"": [], ""bordereaux"": [], ""bordereaux_vendeur"": [], ""factures_clients"": [
            { ""no_fact"": ""00100007"", ""facture_ou_avoir"": ""F"", ""date_fact"": ""2024-04-12"", ""nom"": ""A"", ""lignes"": [] },
            { ""no_fact"": ""00100007"", ""facture_ou_avoir"": ""F"", ""date_fact"": ""2024-04-13"", ""nom"": ""B"", ""lignes"": [] }
          ] }";

        ((Action)(() => EncheresV6FixtureExtractor.FromJson(dup))).Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void ExtractDocuments_filters_by_period()
    {
        EncheresV6FixtureExtractor extractor = EncheresV6FixtureExtractor.FromJson(Snapshot);

        extractor.ExtractDocuments(new DateTime(2025, 1, 1), new DateTime(2026, 1, 1)).Should().BeEmpty();
    }

    [Fact]
    public void SourceName_is_EncheresV6_and_emitter_is_filled_by_platform()
    {
        EncheresV6FixtureExtractor extractor = EncheresV6FixtureExtractor.FromJson(Snapshot);

        extractor.SourceName.Should().Be("EncheresV6");
        extractor.Capabilities.EmitterIdentitySource.Should().Be(EmitterIdentitySource.FilledByPlatform);
    }

    [Fact]
    public void FromJson_rejects_invalid_json()
    {
        ((Action)(() => EncheresV6FixtureExtractor.FromJson("{ not json"))).Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void FromJson_rejects_duplicate_no_ba_for_idempotence()
    {
        // Deux BA de même no_ba produiraient deux pivots de même SourceReference (double-déclaration, R2).
        const string dup = @"{ ""regimes"": [], ""bordereaux"": [
            { ""no_ba"": ""100022"", ""bordereau_ou_avoir"": ""B"", ""date_vente"": ""2024-01-12"", ""nom"": ""A"", ""lignes"": [] },
            { ""no_ba"": ""100022"", ""bordereau_ou_avoir"": ""B"", ""date_vente"": ""2024-01-13"", ""nom"": ""B"", ""lignes"": [] }
          ], ""bordereaux_vendeur"": [] }";

        ((Action)(() => EncheresV6FixtureExtractor.FromJson(dup))).Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void FromJson_rejects_duplicate_no_bv_for_idempotence()
    {
        const string dup = @"{ ""regimes"": [], ""bordereaux"": [], ""bordereaux_vendeur"": [
            { ""no_bv"": ""100012"", ""bordereau_ou_avoir"": ""B"", ""date_vente"": ""2024-01-12"", ""nom"": ""V"", ""code_regime_tva"": ""6"", ""lignes"": [] },
            { ""no_bv"": ""100012"", ""bordereau_ou_avoir"": ""B"", ""date_vente"": ""2024-01-13"", ""nom"": ""V"", ""code_regime_tva"": ""6"", ""lignes"": [] }
          ] }";

        ((Action)(() => EncheresV6FixtureExtractor.FromJson(dup))).Should().Throw<SourceSchemaException>();
    }
}

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

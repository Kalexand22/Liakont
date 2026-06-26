namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;
using Liakont.Agent.Contracts.Pivot;
using Xunit;

/// <summary>
/// Tests SMOKE du nouvel extracteur ODBC BA/BV (<see cref="PervasiveExtractor"/>) sur le modèle marge :
/// streaming des bordereaux acheteur (jambe BuyerFees), LECTURE SEULE STRICTE (aucune écriture, aucune
/// transaction), filtre tenant <c>No_dossier</c>. La couverture fiscale détaillée du mapping vit dans
/// <see cref="EncheresV6RowMapperTests"/> (transformation pure). NB : la suite ODBC exhaustive (avoirs,
/// paiements, régimes, multi-lots) est à reconstruire sur le modèle BA/BV.
/// </summary>
public class PervasiveExtractorTests
{
    private static readonly DateTime From = new DateTime(2024, 1, 1);
    private static readonly DateTime To = new DateTime(2025, 1, 1);

    [Fact]
    public void ExtractDocuments_streams_buyer_bordereau_with_commission_fee()
    {
        var connection = new RecordingConnection(readerResolver: RouteBaThenBv(new[] { MargeBaRow() }));
        var extractor = new PervasiveExtractor(connection, new EncheresV6Schema("enc"), "2", new RecordingAgentLog());

        List<PivotDocumentDto> docs = extractor.ExtractDocuments(From, To).ToList();

        PivotDocumentDto ba = docs.Should().ContainSingle().Subject;
        ba.Number.Should().Be("100022");
        ba.SourceReference.Should().Be("encheresv6:ba:100022");
        ba.Supplier.Should().BeNull("FilledByPlatform : l'agent ne porte pas l'émetteur");
        ba.BuyerFees.Should().ContainSingle();
        ba.BuyerFees![0].NetAmount.Should().Be(401.28m, "commission acheteur TTC = 334.40 + 66.88");
        ba.Lines.Should().ContainSingle().Which.NetAmount.Should().Be(2000.00m);
    }

    [Fact]
    public void Extractor_is_strictly_read_only()
    {
        var connection = new RecordingConnection(readerResolver: RouteBaThenBv(new[] { MargeBaRow() }));
        var extractor = new PervasiveExtractor(connection, new EncheresV6Schema("enc"), "2", new RecordingAgentLog());

        _ = extractor.ExtractDocuments(From, To).ToList();

        connection.NonQueryExecutions.Should().Be(0, "aucune écriture (R1, CLAUDE.md n°5)");
        connection.TransactionsBegun.Should().Be(0, "aucune transaction/verrou");
        connection.ExecutedCommandTexts.Should().OnlyContain(sql => sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Queries_filter_by_dossier_tenant()
    {
        var connection = new RecordingConnection(readerResolver: RouteBaThenBv(Array.Empty<IReadOnlyDictionary<string, object?>>()));
        var extractor = new PervasiveExtractor(connection, new EncheresV6Schema("enc"), "2", new RecordingAgentLog());

        _ = extractor.ExtractDocuments(From, To).ToList();

        connection.ExecutedCommandTexts.Should().Contain(sql => sql.Contains(EncheresV6Schema.ColNoDossierBa));
        connection.ExecutedCommandTexts.Should().Contain(sql => sql.Contains(EncheresV6Schema.ColNoDossierBv));
    }

    [Fact]
    public void Ba_query_joins_ligne_pv_on_global_pv_line_id_not_on_no_ba()
    {
        // BUG-10-EXTRACTION : ligne_pv.no_ba vaut très souvent 0 → l'ancienne jointure (no_ba + no_ligne_pv)
        // ratait et l'adjudication sortait SANS code régime. La VRAIE clé est l'identifiant global de ligne
        // de PV (lignes_ba.no_ligne_tout_pv = ligne_pv.no_ligne_tout_pv).
        var connection = new RecordingConnection(readerResolver: RouteBaThenBv(Array.Empty<IReadOnlyDictionary<string, object?>>()));
        var extractor = new PervasiveExtractor(connection, new EncheresV6Schema("enc"), "2", new RecordingAgentLog());

        _ = extractor.ExtractDocuments(From, To).ToList();

        // La requête ACHETEUR est la seule à joindre ligne_pv (table QUALIFIÉE) ; FC et BV ne la joignent pas.
        string baSql = connection.ExecutedCommandTexts.Single(sql => sql.Contains("enc." + EncheresV6Schema.TableLignePv));
        baSql.Should().Contain(
            "JOIN enc." + EncheresV6Schema.TableLignePv + " lp ON lp." + EncheresV6Schema.ColPvNoLigneToutPv
            + " = l." + EncheresV6Schema.ColNoLigneToutPv,
            "le régime se joint par l'identifiant GLOBAL de ligne de PV");
        baSql.Should().NotContain(
            "lp." + EncheresV6Schema.ColNoBa,
            "ligne_pv.no_ba vaut souvent 0 : il ne doit plus servir de clé de jointure du régime");
    }

    [Fact]
    public void Bv_query_does_not_join_ligne_pv_to_avoid_double_counting_the_commission()
    {
        // Anti-régression (étape 3 du fix) : la résolution du régime VENDEUR reste sur entete_bv.code_regime_tva,
        // SANS jointure ligne_pv (qui produirait un produit cartésien doublant la commission vendeur).
        var connection = new RecordingConnection(readerResolver: RouteBaThenBv(Array.Empty<IReadOnlyDictionary<string, object?>>()));
        var extractor = new PervasiveExtractor(connection, new EncheresV6Schema("enc"), "2", new RecordingAgentLog());

        _ = extractor.ExtractDocuments(From, To).ToList();

        string bvSql = connection.ExecutedCommandTexts.Single(sql => sql.Contains(EncheresV6Schema.TableEnteteBv));

        // « ligne_pv » est un sous-chaîne de « no_ligne_pv » (colonne légitime) : on cible la table QUALIFIÉE.
        bvSql.Should().NotContain("enc." + EncheresV6Schema.TableLignePv, "la jambe vendeur ne joint jamais ligne_pv (anti double-comptage)");
        bvSql.Should().Contain("e." + EncheresV6Schema.ColCodeRegimeTva, "le régime vendeur est lu directement sur entete_bv");
    }

    [Fact]
    public void Buyer_line_carries_regime_resolved_through_global_pv_line_id_even_when_pv_no_ba_is_zero()
    {
        // Cas réel (BA 2000026, lot 22 → no_ligne_tout_pv=287 → ligne_pv code_regime_tva=6, no_ba=0) : le serveur
        // joint le régime par l'identifiant global, donc le pivot ne sort PAS « 0 code régime » (plus de faux blocage).
        var connection = new RecordingConnection(readerResolver: RouteBaThenBv(new[] { MargeBaRow() }));
        var extractor = new PervasiveExtractor(connection, new EncheresV6Schema("enc"), "2", new RecordingAgentLog());

        PivotDocumentDto ba = extractor.ExtractDocuments(From, To).ToList().Should().ContainSingle().Subject;

        ba.Lines.Should().ContainSingle().Which.SourceRegimeCodes.Should().Contain(
            "6", "le régime du lot est résolu par la jointure no_ligne_tout_pv malgré ligne_pv.no_ba=0");
        ba.BuyerFees.Should().ContainSingle().Which.SourceRegimeCode.Should().Be("6");
    }

    [Fact]
    public void ExtractDocuments_streams_facture_client_filtered_by_dossier()
    {
        // Parité ODBC du flux ORDINAIRE : la requête FACTURE CLIENT (entete_facture_clien) streame une facture
        // plate sans frais, code_tva en clé de régime, filtrée par dossier_cpt (tenant).
        var connection = new RecordingConnection(readerResolver: sql =>
            sql.Contains(EncheresV6Schema.TableEnteteFactureClient)
                ? new[] { FactureClientRow() }
                : Array.Empty<IReadOnlyDictionary<string, object?>>());
        var extractor = new PervasiveExtractor(connection, new EncheresV6Schema("enc"), "2", new RecordingAgentLog());

        PivotDocumentDto fc = extractor.ExtractDocuments(From, To).ToList()
            .Should().ContainSingle(d => d.SourceReference.StartsWith("encheresv6:fc:", StringComparison.Ordinal)).Subject;

        fc.Number.Should().Be("00100007");
        fc.BuyerFees.Should().BeNull("une facture ordinaire ne porte aucun frais d'enchères");
        fc.SellerFees.Should().BeNull();
        fc.OperationCategory.Should().BeNull("la nature est plateforme (profil tenant)");
        fc.Lines.Should().ContainSingle().Which.NetAmount.Should().Be(144.00m);
        fc.Lines[0].Taxes[0].TaxAmount.Should().Be(28.80m);
        fc.Lines[0].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("1");

        connection.ExecutedCommandTexts.Should().Contain(
            sql => sql.Contains(EncheresV6Schema.TableEnteteFactureClient) && sql.Contains(EncheresV6Schema.ColDossierCpt),
            "la requête facture client filtre par dossier_cpt (tenant)");
    }

    private static Dictionary<string, object?> FactureClientRow() => new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [EncheresV6Schema.ColNoFact] = "00100007",
        [EncheresV6Schema.ColFactureOuAvoir] = "F",
        [EncheresV6Schema.ColDateFact] = new DateTime(2024, 4, 12),
        [EncheresV6Schema.ColNoFactureLettrage] = null,
        [EncheresV6Schema.ColNom] = "LOBRY",
        [EncheresV6Schema.ColPrenom] = "STEEVE",
        [EncheresV6Schema.ColFcAdresse1] = "15 rue Boberie",
        [EncheresV6Schema.ColFcCp] = "53000",
        [EncheresV6Schema.ColVille] = "LAVAL",
        [EncheresV6Schema.ColCodePays] = "FR",
        [EncheresV6Schema.ColFcMontantHt] = 144.0d,
        [EncheresV6Schema.ColFcMontantTva] = 28.8d,
        [EncheresV6Schema.ColFcMontantTtc] = 172.8d,
        [EncheresV6Schema.ColCodeDevise] = "EUR",
        [EncheresV6Schema.ColOriginNoFact] = null,
        [EncheresV6Schema.ColOriginDateFact] = null,
        [EncheresV6Schema.ColTypeLigne] = "1",
        [EncheresV6Schema.ColNoLigne] = "1",
        [EncheresV6Schema.ColCodeArticle] = "CV",
        [EncheresV6Schema.ColDesignation] = "Caisse de Vins",
        [EncheresV6Schema.ColQte] = 12,
        [EncheresV6Schema.ColPrixUnitaireHt] = 12.0d,
        [EncheresV6Schema.ColFcCodeTva] = 1,
        [EncheresV6Schema.ColTauxTva] = 20.0d,
    };

    // Route la requête ACHETEUR (seule à référencer entete_ba) vers les lignes fournies ; les requêtes VENDEUR
    // (entete_bv) et FACTURE CLIENT (entete_facture_clien) reçoivent un jeu vide. « entete_ba » n'est sous-chaîne
    // ni d'« entete_bv » ni d'« entete_facture_clien » : le routage est sans ambiguïté.
    private static Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> RouteBaThenBv(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> baRows) =>
        sql => sql.Contains(EncheresV6Schema.TableEnteteBa)
            ? baRows
            : Array.Empty<IReadOnlyDictionary<string, object?>>();

    private static Dictionary<string, object?> MargeBaRow() => new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [EncheresV6Schema.ColNoBa] = "100022",
        [EncheresV6Schema.ColBordereauOuAvoir] = "B",
        [EncheresV6Schema.ColDateVente] = new DateTime(2024, 1, 12),
        [EncheresV6Schema.ColNoBaLettrage] = null,
        [EncheresV6Schema.ColNom] = "Acheteur Particulier (fictif)",
        [EncheresV6Schema.ColPrenom] = null,
        [EncheresV6Schema.ColSociete] = null,
        [EncheresV6Schema.ColAcheteurSiren] = null,
        [EncheresV6Schema.ColTvaCee] = null,
        [EncheresV6Schema.ColAdresse] = null,
        [EncheresV6Schema.ColCodePostal] = "35000",
        [EncheresV6Schema.ColVille] = "Rennes",
        [EncheresV6Schema.ColCodePays] = "FR",
        [EncheresV6Schema.ColCodeExport] = false,
        [EncheresV6Schema.ColModeLivraison] = null,
        [EncheresV6Schema.ColTotalBordereau] = 2401.28d,
        [EncheresV6Schema.ColOriginNoBa] = null,
        [EncheresV6Schema.ColOriginDateVente] = null,
        [EncheresV6Schema.ColTypeLigne] = "1",
        [EncheresV6Schema.ColCodeLigne] = "6",
        [EncheresV6Schema.ColNoLignePv] = "1",
        [EncheresV6Schema.ColNoLigneToutPv] = "287",
        [EncheresV6Schema.ColLibelleLigne] = "Adjudication lot 1",
        [EncheresV6Schema.ColMontantAdjHt] = 2000.00d,
        [EncheresV6Schema.ColMttTvaInclusAdj] = 0.0d,
        [EncheresV6Schema.ColMttTvaEnPlusAdj] = 0.0d,
        [EncheresV6Schema.ColMontantFraisHt] = 334.40d,
        [EncheresV6Schema.ColMontantTvaFrais] = 66.88d,
        [EncheresV6Schema.ColCodeDevise] = "EUR",
        [EncheresV6Schema.ColCodeRegime] = "6",
    };
}

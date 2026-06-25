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

        string baSql = connection.ExecutedCommandTexts.Single(sql => !sql.Contains(EncheresV6Schema.TableEnteteBv));
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

    // Route la requête BA vers les lignes fournies et la requête BV vers un jeu vide (la requête vendeur
    // référence entete_bv ; la requête acheteur ne le fait pas).
    private static Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> RouteBaThenBv(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> baRows) =>
        sql => sql.Contains(EncheresV6Schema.TableEnteteBv)
            ? Array.Empty<IReadOnlyDictionary<string, object?>>()
            : baRows;

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

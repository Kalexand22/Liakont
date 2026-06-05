namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Extraction;
using Newtonsoft.Json;
using Xunit;

/// <summary>
/// Tests du <see cref="PervasiveExtractor"/> (extraction ODBC réelle des documents, ADP02) avec un
/// lecteur de données MOCKÉ et un espion de commandes (<see cref="RecordingConnection"/>). Couvre :
/// le regroupement par bordereau en streaming, le mapping pivot via la classe partagée, la PREUVE de
/// lecture seule stricte (aucune commande non-SELECT, aucune transaction, aucun verrou), les erreurs
/// typées du contrat (R7) et les méthodes différées à ADP03/04/05.
/// </summary>
public class PervasiveExtractorTests
{
    private static readonly DateTime PeriodFrom = new DateTime(2026, 1, 1);
    private static readonly DateTime PeriodTo = new DateTime(2026, 3, 1);

    [Fact]
    public void SourceName_is_EncheresV6()
    {
        Extractor(Connection()).SourceName.Should().Be("EncheresV6");
    }

    [Fact]
    public void GetInfo_describes_the_odbc_adapter()
    {
        ExtractorInfo info = Extractor(Connection()).GetInfo();

        info.Name.Should().Be("EncheresV6");
        info.Version.Should().Be("1.0.0-odbc");
        info.TargetSystem.Should().Contain("ODBC");
    }

    [Fact]
    public void Capabilities_declare_what_ADP03_honours()
    {
        ExtractorCapabilities caps = Extractor(Connection()).Capabilities;

        caps.HasDetailedLines.Should().BeTrue();
        caps.HasStoredHeaderTotal.Should().BeTrue();
        caps.RegimeKeyShape.Should().Be(RegimeKeyShape.Simple);
        caps.EmitterIdentitySource.Should().Be(EmitterIdentitySource.FromConfig);
        caps.HasCreditNoteLink.Should().BeTrue("les avoirs (lien no_ba_lettrage → origine) sont livrés par ADP03");
        caps.ExposesPayments.Should().BeTrue("les encaissements (lignes type 3, F09) sont livrés par ADP03");
        caps.ProvidesSourceDocuments.Should().BeFalse("false par défaut : aucune source PDF configurée (ADP05 pilote la capacité par config)");
        caps.ProvidesUnlinkedDocumentPool.Should().BeFalse();
    }

    [Fact]
    public void ExtractDocuments_groups_lines_by_bordereau_into_pivot_documents()
    {
        var connection = Connection(TwoSalesWithTwoLinesEach());

        List<PivotDocumentDto> docs = Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo).ToList();

        docs.Should().HaveCount(2);
        docs.Select(d => d.SourceReference).Should().Equal("no_ba=4500", "no_ba=4501");
        docs.Should().OnlyContain(d => d.SourceDocumentKind == "B");
        docs.Should().OnlyContain(d => d.Lines.Count == 2);
        docs[0].Supplier.Siren.Should().Be("111111111", "le SIREN vient du paramétrage tenant, pas de la base");
    }

    [Fact]
    public void ExtractDocuments_keeps_regime_raw_and_does_not_map_tva()
    {
        var connection = Connection(TwoSalesWithTwoLinesEach());

        PivotDocumentDto sale = Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo)
            .Single(d => d.SourceReference == "no_ba=4500");

        PivotLineDto adjudication = sale.Lines[0];
        adjudication.SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("5");
        adjudication.Taxes[0].CategoryCode.Should().BeNull("le mapping catégorie/VATEX est plateforme (R3)");
        adjudication.Taxes[0].VatexCode.Should().BeNull();
    }

    [Fact]
    public void ExtractDocuments_rounds_dirty_legacy_floats_half_up()
    {
        var connection = Connection(TwoSalesWithTwoLinesEach());

        PivotDocumentDto sale = Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo)
            .Single(d => d.SourceReference == "no_ba=4500");

        // Frais bruts 8.329999999999998 → 8.33 (arrondi commercial half-up, comme le mode fixtures).
        sale.Lines[1].NetAmount.Should().Be(8.33m);
    }

    [Fact]
    public void ExtractDocuments_pro_buyer_sets_company_hint_from_societe_column()
    {
        var connection = Connection(TwoSalesWithTwoLinesEach());

        List<PivotDocumentDto> docs = Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo).ToList();

        docs.Single(d => d.SourceReference == "no_ba=4501").Customer!.IsCompanyHint
            .Should().BeTrue("la colonne societe est renseignée");
        docs.Single(d => d.SourceReference == "no_ba=4500").Customer!.IsCompanyHint
            .Should().BeFalse("societe est vide");
    }

    [Fact]
    public void ExtractDocuments_is_read_only_no_write_command_transaction_or_lock()
    {
        var connection = Connection(TwoSalesWithTwoLinesEach());

        List<PivotDocumentDto> docs = Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo).ToList();

        docs.Should().HaveCount(2);
        connection.ExecutedCommandTexts.Should().NotBeEmpty();
        connection.ExecutedCommandTexts.Should().OnlyContain(
            t => t.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase),
            "lecture seule stricte : aucune commande non-SELECT n'est émise (CLAUDE.md n°5)");
        connection.NonQueryExecutions.Should().Be(0, "aucun INSERT/UPDATE/DELETE n'est émis");
        connection.TransactionsBegun.Should().Be(0, "aucune transaction d'écriture ni verrou explicite");
    }

    [Fact]
    public void ExtractDocuments_query_matches_documented_schema_and_binds_the_period()
    {
        var connection = Connection(TwoSalesWithTwoLinesEach());

        List<PivotDocumentDto> docs = Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo).ToList();

        docs.Should().HaveCount(2);
        RecordingCommand command = connection.Commands.Single();
        command.CommandText.Should().Be(EncheresV6Schema.SelectDocumentsSql);
        command.CommandText.Should().Contain("bordereau_ou_avoir IN ('B', 'A')", "ADP03 extrait aussi les avoirs");
        command.CommandText.Should().Contain("type_ligne IN ('4', '2')");
        command.CommandText.Should().Contain("LEFT JOIN");
        command.CommandText.Should().Contain("o.no_ba = e.no_ba_lettrage", "auto-jointure de l'entête d'origine d'un avoir");
        command.CommandText.Should().Contain("AS origin_no_ba");
        command.CommandText.Should().Contain("e.date_vente >= ? AND e.date_vente < ?");
        command.Parameters.Count.Should().Be(2, "période bornée par deux paramètres positionnels");
        ((FakeParameter)command.Parameters[0]!).Value.Should().Be(PeriodFrom);
        ((FakeParameter)command.Parameters[1]!).Value.Should().Be(PeriodTo);
    }

    [Fact]
    public void ExtractDocuments_is_idempotent_across_two_extractions()
    {
        var connection = Connection(TwoSalesWithTwoLinesEach());
        PervasiveExtractor extractor = Extractor(connection);

        IEnumerable<string> first = extractor.ExtractDocuments(PeriodFrom, PeriodTo).Select(d => d.SourceReference);
        IEnumerable<string> second = extractor.ExtractDocuments(PeriodFrom, PeriodTo).Select(d => d.SourceReference);

        first.Should().Equal(second);
    }

    [Fact]
    public void ExtractDocuments_throws_SourceUnavailable_when_connection_open_fails()
    {
        var connection = Connection(openException: new FakeDbException("DSN=cmp;PWD=secret-injoignable"));

        Action act = () => Drain(Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo));

        act.Should().Throw<SourceUnavailableException>()
            .Which.Message.Should().NotContain("secret-injoignable", "le message opérateur ne fuite jamais la chaîne de connexion/le détail technique (CLAUDE.md n°10)");
    }

    [Fact]
    public void ExtractDocuments_throws_SourceSchema_when_expected_column_is_missing()
    {
        var rowWithoutTotal = SaleRow("4600", "F-2026-0600", null, null, "4", "Lot", 100.0, 20.0, "5", "ligne#1");
        rowWithoutTotal.Remove(EncheresV6Schema.ColTotalBordereau);

        var connection = Connection(new[] { rowWithoutTotal });

        Action act = () => Drain(Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo));

        act.Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void ExtractDocuments_throws_SourceSchema_when_no_ba_is_blank()
    {
        var rowWithBlankKey = SaleRow(string.Empty, "F-2026-0700", null, null, "4", "Lot", 100.0, 20.0, "5", "ligne#1");

        var connection = Connection(new[] { rowWithBlankKey });

        Action act = () => Drain(Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo));

        act.Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void ExtractDocuments_emits_sale_with_no_document_lines_without_dropping_it()
    {
        // Ligne d'entête seule (LEFT JOIN sans ligne 4/2 correspondante) : colonnes ligne à NULL.
        var headerOnly = SaleRow("4800", "F-2026-0800", null, null, "4", "x", 0.0, 0.0, "5", "ligne#1");
        headerOnly[EncheresV6Schema.ColTypeLigne] = null;
        headerOnly[EncheresV6Schema.ColDesignation] = null;
        headerOnly[EncheresV6Schema.ColMontantHt] = null;
        headerOnly[EncheresV6Schema.ColMontantTva] = null;
        headerOnly[EncheresV6Schema.ColTauxTva] = null;
        headerOnly[EncheresV6Schema.ColQuantite] = null;
        headerOnly[EncheresV6Schema.ColPrixUnitaire] = null;
        headerOnly[EncheresV6Schema.ColCodeRegime] = null;
        headerOnly[EncheresV6Schema.ColNoLigne] = null;

        PivotDocumentDto doc = Extractor(Connection(new[] { headerOnly }))
            .ExtractDocuments(PeriodFrom, PeriodTo)
            .Single();

        doc.SourceReference.Should().Be("no_ba=4800");
        doc.Lines.Should().BeEmpty("une vente sans ligne 4/2 est émise (jamais omise silencieusement), pas droppée");
    }

    [Fact]
    public void ExtractDocuments_accepts_null_optional_line_fields()
    {
        var row = SaleRow("4700", "F-2026-0700", null, null, "4", "Lot", 100.0, 20.0, "5", "ligne#1");
        row[EncheresV6Schema.ColTauxTva] = null;
        row[EncheresV6Schema.ColQuantite] = null;
        row[EncheresV6Schema.ColPrixUnitaire] = null;

        PivotDocumentDto doc = Extractor(Connection(new[] { row }))
            .ExtractDocuments(PeriodFrom, PeriodTo)
            .Single();

        PivotLineDto line = doc.Lines.Single();
        line.Quantity.Should().Be(1m, "le mapper applique la quantité par défaut quand la source est nulle");
        line.UnitPriceNet.Should().BeNull();
        line.Taxes[0].Rate.Should().BeNull();
    }

    [Fact]
    public void ExtractDocuments_throws_SourceSchema_when_date_vente_is_null()
    {
        var row = SaleRow("4710", "F-2026-0710", null, null, "4", "Lot", 100.0, 20.0, "5", "ligne#1");
        row[EncheresV6Schema.ColDateVente] = null;

        Action act = () => Drain(Extractor(Connection(new[] { row })).ExtractDocuments(PeriodFrom, PeriodTo));

        act.Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void ExtractDocuments_throws_SourceSchema_when_amount_is_not_numeric()
    {
        var row = SaleRow("4720", "F-2026-0720", null, null, "4", "Lot", 100.0, 20.0, "5", "ligne#1");
        row[EncheresV6Schema.ColMontantHt] = "pas-un-nombre";

        Action act = () => Drain(Extractor(Connection(new[] { row })).ExtractDocuments(PeriodFrom, PeriodTo));

        act.Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void ExtractDocuments_translates_read_failure_to_SourceUnavailable()
    {
        var row = SaleRow("4730", "F-2026-0730", null, null, "4", "Lot", 100.0, 20.0, "5", "ligne#1");
        row[EncheresV6Schema.ColTotalHt] = new FakeDbException("lecture interrompue");

        Action act = () => Drain(Extractor(Connection(new[] { row })).ExtractDocuments(PeriodFrom, PeriodTo));

        act.Should().Throw<SourceUnavailableException>();
    }

    [Fact]
    public void CheckHealth_is_healthy_when_expected_tables_are_present()
    {
        var connection = Connection(scalarResolver: CountResolver(new Dictionary<string, long>
        {
            ["entete_ba"] = 12,
            ["lignes_ba"] = 34,
            ["Regime_tva"] = 2,
        }));

        HealthCheckResult result = Extractor(connection).CheckHealth();

        result.IsHealthy.Should().BeTrue();
        result.Message.Should().Contain("entete_ba (12)").And.Contain("Regime_tva (2)");
        connection.ExecutedCommandTexts.Should().OnlyContain(
            t => t.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));
        connection.NonQueryExecutions.Should().Be(0);
    }

    [Fact]
    public void CheckHealth_is_unhealthy_and_names_the_missing_table()
    {
        var connection = Connection(scalarResolver: CountResolver(new Dictionary<string, long>
        {
            ["entete_ba"] = 12,

            // lignes_ba absente → FakeDbException levée par le résolveur.
            ["Regime_tva"] = 2,
        }));

        HealthCheckResult result = Extractor(connection).CheckHealth();

        result.IsHealthy.Should().BeFalse();
        result.Message.Should().Contain("lignes_ba");
    }

    [Fact]
    public void CheckHealth_is_unhealthy_when_connection_cannot_open()
    {
        var connection = Connection(openException: new FakeDbException("pilote absent"));

        HealthCheckResult result = Extractor(connection).CheckHealth();

        result.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void ExtractDocuments_includes_credit_notes_with_link_to_origin_invoice()
    {
        // L'avoir 4600 lettré au bordereau d'origine 4500 (colonnes origin_* via l'auto-jointure).
        var connection = Connection(SaleAndCreditNote());

        List<PivotDocumentDto> docs = Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo).ToList();

        PivotDocumentDto avoir = docs.Single(d => d.SourceDocumentKind == "A");
        avoir.SourceReference.Should().Be("no_ba=4600");
        avoir.CreditNoteRefs.Should().ContainSingle("F07-F08 : un avoir référence sa facture d'origine (BT-25)");
        avoir.CreditNoteRefs[0].Number.Should().Be("F-2026-0500");
        avoir.CreditNoteRefs[0].IssueDate.Should().Be(new DateTime(2026, 1, 12));
        avoir.CreditNoteRefs[0].SourceReference.Should().Be("no_ba=4500");
        avoir.Totals.TotalGross.Should().Be(130.00m, "le sens « crédit » vient du type, jamais du signe (montants positifs)");
    }

    [Fact]
    public void ExtractDocuments_blocks_credit_note_whose_lettrage_does_not_resolve()
    {
        // Avoir de type 'A' SANS origine résoluble (origin_* NULL) : bloqué, jamais deviné (ADR-0004 D3-3,
        // F07-F08 §B.4) — parité avec le mode fixtures qui laisse le mapper bloquer.
        var orphan = CreditNoteRow("4601", "AV-2026-0008", lettrage: "9999", originNoBa: null, originNumeroPiece: null, originDateVente: null, "4", "Annulation lot", 100.0, 20.0, "5", "ligne#1");

        Action act = () => Drain(Extractor(Connection(new[] { orphan })).ExtractDocuments(PeriodFrom, PeriodTo));

        act.Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void ExtractDocuments_blocks_credit_note_whose_origin_date_is_missing()
    {
        // Origine RÉSOLUE (origin_no_ba + origin_numero_piece présents) mais SANS date d'émission
        // (origin_date_vente NULL) : la date de référence d'avoir (amended_date, BT-25) est obligatoire et ne
        // doit jamais être fabriquée (0001-01-01). L'avoir est bloqué, jamais envoyé faux (ADR-0004 D3-3).
        var missingOriginDate = CreditNoteRow("4602", "AV-2026-0009", lettrage: "4500", originNoBa: "4500", originNumeroPiece: "F-2026-0500", originDateVente: null, "4", "Annulation lot", 100.0, 20.0, "5", "ligne#1");

        Action act = () => Drain(Extractor(Connection(new[] { missingOriginDate })).ExtractDocuments(PeriodFrom, PeriodTo));

        act.Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void ExtractDocuments_reads_credit_notes_read_only()
    {
        var connection = Connection(SaleAndCreditNote());

        Drain(Extractor(connection).ExtractDocuments(PeriodFrom, PeriodTo));

        connection.ExecutedCommandTexts.Should().OnlyContain(
            t => t.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));
        connection.NonQueryExecutions.Should().Be(0);
        connection.TransactionsBegun.Should().Be(0);
    }

    [Fact]
    public void ExtractPayments_returns_raw_payment_attached_to_its_bordereau()
    {
        var connection = Connection(OnePayment());

        List<PivotPaymentDto> payments = Extractor(connection).ExtractPayments(PeriodFrom, PeriodTo).ToList();

        payments.Should().ContainSingle();
        PivotPaymentDto payment = payments[0];
        payment.PaymentDate.Should().Be(new DateTime(2026, 1, 15));
        payment.Amount.Should().Be(130.00m);
        payment.Method.Should().Be("CB");
        payment.RelatedDocumentNumber.Should().Be("F-2026-0500", "rattachement au bordereau via no_ba (INNER JOIN)");
        payment.SourceReference.Should().Be("no_remise=REM-0500");
    }

    [Fact]
    public void ExtractPayments_query_targets_type3_lines_and_binds_the_period()
    {
        var connection = Connection(OnePayment());

        Drain(Extractor(connection).ExtractPayments(PeriodFrom, PeriodTo));

        RecordingCommand command = connection.Commands.Single();
        command.CommandText.Should().Be(EncheresV6Schema.SelectPaymentsSql);
        command.CommandText.Should().Contain("type_ligne = '3'");
        command.CommandText.Should().Contain("date_reglement >= ? AND l.date_reglement < ?");
        command.Parameters.Count.Should().Be(2);
        ((FakeParameter)command.Parameters[0]!).Value.Should().Be(PeriodFrom);
        ((FakeParameter)command.Parameters[1]!).Value.Should().Be(PeriodTo);
    }

    [Fact]
    public void ExtractPayments_is_read_only_no_write_command_or_transaction()
    {
        var connection = Connection(OnePayment());

        Drain(Extractor(connection).ExtractPayments(PeriodFrom, PeriodTo));

        connection.ExecutedCommandTexts.Should().OnlyContain(
            t => t.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));
        connection.NonQueryExecutions.Should().Be(0);
        connection.TransactionsBegun.Should().Be(0);
    }

    [Fact]
    public void Fixtures_and_odbc_produce_identical_pivot_for_the_same_dataset()
    {
        // Acceptance ADP03 : « test de parité fixtures vs ODBC mocké sur le même jeu de données ». Un SEUL
        // jeu de données (vente + avoir lettré + règlement) dérive les DEUX chemins : le JSON de fixtures et
        // les lignes du lecteur ODBC routées par requête. La preuve de parité est l'empreinte canonique.
        EncheresV6SourceSnapshot dataset = ParityDataset();

        EncheresV6FixtureExtractor fixtures = EncheresV6FixtureExtractor.FromJson(
            JsonConvert.SerializeObject(dataset), Emitter(), OperationCategory.LivraisonBiens);
        var odbc = new PervasiveExtractor(ParityConnection(dataset), Emitter(), OperationCategory.LivraisonBiens);

        Dictionary<string, PivotDocumentDto> fixtureDocs =
            fixtures.ExtractDocuments(PeriodFrom, PeriodTo).ToDictionary(d => d.SourceReference);
        Dictionary<string, PivotDocumentDto> odbcDocs =
            odbc.ExtractDocuments(PeriodFrom, PeriodTo).ToDictionary(d => d.SourceReference);

        odbcDocs.Keys.Should().BeEquivalentTo(fixtureDocs.Keys);
        odbcDocs.Should().HaveCount(2, "1 vente + 1 avoir");
        foreach (string sourceReference in fixtureDocs.Keys)
        {
            PayloadHasher.ComputeHash(odbcDocs[sourceReference])
                .Should().Be(
                    PayloadHasher.ComputeHash(fixtureDocs[sourceReference]),
                    "fixtures et ODBC produisent un pivot byte-à-byte identique pour " + sourceReference);
        }

        List<PivotPaymentDto> fixturePayments = fixtures.ExtractPayments(PeriodFrom, PeriodTo).ToList();
        List<PivotPaymentDto> odbcPayments = odbc.ExtractPayments(PeriodFrom, PeriodTo).ToList();

        odbcPayments.Should().ContainSingle();
        odbcPayments[0].Should().BeEquivalentTo(fixturePayments[0], "même encaissement brut dans les deux chemins");
    }

    [Fact]
    public void ListSourceTaxRegimes_returns_declared_regimes_with_occurrences()
    {
        var connection = Connection(regimeRows: new[]
        {
            RegimeRow("5", "Assujetti normal", 5),
            RegimeRow("6", "Régime de la marge", 1),
            RegimeRow("9", "Régime jamais utilisé", 0),
        });

        IReadOnlyList<SourceTaxRegimeDto> regimes = Extractor(connection).ListSourceTaxRegimes();

        regimes.Should().HaveCount(3);
        regimes.Single(r => r.Code == "5").Occurrences.Should().Be(5);
        regimes.Single(r => r.Code == "6").Label.Should().Be("Régime de la marge");
        regimes.Single(r => r.Code == "9").Occurrences
            .Should().Be(0, "un régime déclaré mais jamais utilisé ressort à 0, jamais omis (LEFT JOIN)");
    }

    [Fact]
    public void ListSourceTaxRegimes_keeps_codes_raw_without_interpreting_them()
    {
        var connection = Connection(regimeRows: new[] { RegimeRow("6", "Régime de la marge", 3) });

        SourceTaxRegimeDto regime = Extractor(connection).ListSourceTaxRegimes().Single();

        regime.Code.Should().Be("6", "le code régime est transporté brut, jamais mappé (R3, CLAUDE.md n°2)");
        regime.Label.Should().Be("Régime de la marge");
        regime.Occurrences.Should().Be(3);
    }

    [Fact]
    public void ListSourceTaxRegimes_query_matches_documented_schema_and_is_read_only()
    {
        var connection = Connection(regimeRows: new[] { RegimeRow("5", "Assujetti normal", 2) });

        Extractor(connection).ListSourceTaxRegimes();

        RecordingCommand command = connection.Commands.Single();
        command.CommandText.Should().Be(EncheresV6Schema.SelectTaxRegimesSql);
        command.CommandText.Should().Contain("FROM Regime_tva");
        command.CommandText.Should().Contain("LEFT JOIN lignes_ba");
        command.CommandText.Should().Contain("GROUP BY");
        connection.ExecutedCommandTexts.Should().OnlyContain(
            t => t.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase),
            "lecture seule stricte (CLAUDE.md n°5)");
        connection.NonQueryExecutions.Should().Be(0, "aucun INSERT/UPDATE/DELETE n'est émis");
        connection.TransactionsBegun.Should().Be(0, "aucune transaction d'écriture ni verrou");
    }

    [Fact]
    public void ListSourceTaxRegimes_throws_SourceUnavailable_when_connection_open_fails()
    {
        var connection = Connection(openException: new FakeDbException("DSN=cmp;PWD=secret-injoignable"));

        Action act = () => Extractor(connection).ListSourceTaxRegimes();

        act.Should().Throw<SourceUnavailableException>()
            .Which.Message.Should().NotContain("secret-injoignable", "le message opérateur ne fuite jamais la chaîne de connexion (CLAUDE.md n°10)");
    }

    [Fact]
    public void ListSourceTaxRegimes_skips_blank_code_regime_rows_like_fixture_mode()
    {
        var connection = Connection(regimeRows: new[]
        {
            RegimeRow("5", "Assujetti normal", 4),
            RegimeRow("   ", "Entrée sans code", 0),
        });

        IReadOnlyList<SourceTaxRegimeDto> regimes = Extractor(connection).ListSourceTaxRegimes();

        regimes.Should().ContainSingle("une entrée Regime_tva à code vide est ignorée, jamais bloquée (parité avec le mode fixtures)");
        regimes[0].Code.Should().Be("5");
    }

    [Fact]
    public void ListSourceTaxRegimes_throws_SourceSchema_when_occurrences_is_not_numeric()
    {
        var badRow = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [EncheresV6Schema.ColCodeRegime] = "5",
            [EncheresV6Schema.ColLibelleRegime] = "Assujetti normal",
            [EncheresV6Schema.ColRegimeOccurrences] = "pas-un-nombre",
        };
        var connection = Connection(regimeRows: new[] { badRow });

        Action act = () => Extractor(connection).ListSourceTaxRegimes();

        act.Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void GetAttachments_and_pool_are_empty_without_a_configured_pdf_source()
    {
        // Défaut (aucune source PDF en config) : null-object → capacités false, listes vides (ADP05).
        PervasiveExtractor extractor = Extractor(Connection());

        extractor.Capabilities.ProvidesSourceDocuments.Should().BeFalse();
        extractor.Capabilities.ProvidesUnlinkedDocumentPool.Should().BeFalse();
        extractor.GetAttachments("no_ba=4500").Should().BeEmpty();
        extractor.ListPoolDocuments(PeriodFrom, PeriodTo).Should().BeEmpty();
    }

    [Fact]
    public void GetAttachments_and_pool_delegate_to_the_configured_pdf_source()
    {
        // ADP05 : la capacité PDF est portée par la config (source PDF injectée), JAMAIS par l'extraction ODBC
        // (qui reste en lecture seule stricte des tables documentaires). On prouve la délégation + les capacités.
        var attachment = new SourceAttachment("no_ba=4500", @"C:\pdf\bordereau-4500.pdf");
        var pool = new PoolDocument("scan-1.pdf", @"C:\pool\scan-1.pdf");
        var stub = new StubPdfSource(
            providesSourceDocuments: true,
            providesUnlinkedDocumentPool: true,
            attachments: new[] { attachment },
            pool: new[] { pool });

        var extractor = new PervasiveExtractor(Connection(), Emitter(), OperationCategory.LivraisonBiens, stub);

        extractor.Capabilities.ProvidesSourceDocuments.Should().BeTrue();
        extractor.Capabilities.ProvidesUnlinkedDocumentPool.Should().BeTrue();
        extractor.GetAttachments("no_ba=4500").Should().ContainSingle().Which.Should().BeSameAs(attachment);
        stub.LastRequestedReference.Should().Be("no_ba=4500");
        extractor.ListPoolDocuments(PeriodFrom, PeriodTo).Should().ContainSingle().Which.Should().BeSameAs(pool);
    }

    [Fact]
    public void ReadOnly_guard_rejects_non_select_and_accepts_documented_queries()
    {
        Action write = () => EncheresV6Schema.EnsureSelectOnly("UPDATE entete_ba SET total_ht = 0");
        write.Should().Throw<InvalidOperationException>();

        Action delete = () => EncheresV6Schema.EnsureSelectOnly("  delete from lignes_ba");
        delete.Should().Throw<InvalidOperationException>();

        Action documents = () => EncheresV6Schema.EnsureSelectOnly(EncheresV6Schema.SelectDocumentsSql);
        documents.Should().NotThrow();

        Action count = () => EncheresV6Schema.EnsureSelectOnly(EncheresV6Schema.CountSql(EncheresV6Schema.TableEntete));
        count.Should().NotThrow();
    }

    private static EncheresV6EmitterIdentity Emitter() =>
        new EncheresV6EmitterIdentity(
            name: "Étude Fictïve SVV",
            siren: "111111111",
            city: "Rennes",
            postalCode: "35000",
            countryCode: "FR");

    private static PervasiveExtractor Extractor(RecordingConnection connection) =>
        new PervasiveExtractor(connection, Emitter(), OperationCategory.LivraisonBiens);

    private static RecordingConnection Connection(
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows = null,
        Func<string, object?>? scalarResolver = null,
        Exception? openException = null,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? regimeRows = null)
    {
        // Confort de test : `regimeRows` alimente le résolveur de lecteur partagé (approche d'ADP03) — la
        // requête de listage des régimes (SelectTaxRegimesSql) reçoit ces lignes, toute autre requête reçoit
        // les lignes de documents.
        Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>? readerResolver =
            regimeRows is null
                ? (Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>?)null
                : commandText => string.Equals(commandText, EncheresV6Schema.SelectTaxRegimesSql, StringComparison.Ordinal)
                    ? regimeRows
                    : rows ?? Array.Empty<IReadOnlyDictionary<string, object?>>();

        return new RecordingConnection(rows, scalarResolver, openException, readerResolver);
    }

    private static Dictionary<string, object?> RegimeRow(string code, string? libelle, int occurrences) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [EncheresV6Schema.ColCodeRegime] = code,
            [EncheresV6Schema.ColLibelleRegime] = libelle,
            [EncheresV6Schema.ColRegimeOccurrences] = occurrences,
        };

    private static Func<string, object?> CountResolver(IReadOnlyDictionary<string, long> counts) =>
        commandText =>
        {
            foreach (KeyValuePair<string, long> entry in counts)
            {
                if (commandText.Contains(entry.Key))
                {
                    return entry.Value;
                }
            }

            throw new FakeDbException("table introuvable : " + commandText);
        };

    private static void Drain<T>(IEnumerable<T> items)
    {
        foreach (T item in items)
        {
            _ = item;
        }
    }

    private static Dictionary<string, object?>[] TwoSalesWithTwoLinesEach()
    {
        return new[]
        {
            // Bordereau 4500 — acheteur particulier (societe vide), frais « sales » (8.329999999999998).
            SaleRow("4500", "F-2026-0500", null, null, "4", "Adjudication lot 9", 100.00, 20.00, "5", "ligne#1"),
            SaleRow("4500", "F-2026-0500", null, null, "2", "Frais acheteur", 8.329999999999998, 1.666, "5", "ligne#2"),

            // Bordereau 4501 — acheteur professionnel (societe + SIREN renseignés).
            SaleRow("4501", "F-2026-0501", "Galerie Cliente SARL (fictif)", "523456789", "4", "Adjudication lot 12", 2500.00, 500.00, "5", "ligne#1"),
            SaleRow("4501", "F-2026-0501", "Galerie Cliente SARL (fictif)", "523456789", "2", "Frais acheteur", 250.00, 50.00, "5", "ligne#2"),
        };
    }

    private static Dictionary<string, object?> SaleRow(
        string noBa,
        string numeroPiece,
        string? societe,
        string? acheteurSiren,
        string typeLigne,
        string designation,
        double montantHt,
        double montantTva,
        string codeRegime,
        string noLigne)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [EncheresV6Schema.ColNoBa] = noBa,
            [EncheresV6Schema.ColNumeroPiece] = numeroPiece,
            [EncheresV6Schema.ColBordereauOuAvoir] = "B",
            [EncheresV6Schema.ColDateVente] = new DateTime(2026, 1, 12),
            [EncheresV6Schema.ColNoBaLettrage] = null,
            [EncheresV6Schema.ColAcheteurNom] = "Acheteur (fictif)",
            [EncheresV6Schema.ColSociete] = societe,
            [EncheresV6Schema.ColAcheteurSiren] = acheteurSiren,
            [EncheresV6Schema.ColAcheteurVille] = "Rennes",
            [EncheresV6Schema.ColAcheteurCodePostal] = "35000",
            [EncheresV6Schema.ColAcheteurPays] = "FR",
            [EncheresV6Schema.ColTotalHt] = 108.33,
            [EncheresV6Schema.ColTotalTva] = 21.67,
            [EncheresV6Schema.ColTotalBordereau] = 130.00,
            [EncheresV6Schema.ColTypeLigne] = typeLigne,
            [EncheresV6Schema.ColDesignation] = designation,
            [EncheresV6Schema.ColMontantHt] = montantHt,
            [EncheresV6Schema.ColMontantTva] = montantTva,
            [EncheresV6Schema.ColTauxTva] = 20.0,
            [EncheresV6Schema.ColQuantite] = 1.0,
            [EncheresV6Schema.ColPrixUnitaire] = montantHt,
            [EncheresV6Schema.ColCodeRegime] = codeRegime,
            [EncheresV6Schema.ColNoLigne] = noLigne,
        };
    }

    private static Dictionary<string, object?>[] SaleAndCreditNote()
    {
        return new[]
        {
            SaleRow("4500", "F-2026-0500", null, null, "4", "Adjudication lot 9", 100.00, 20.00, "5", "ligne#1"),
            SaleRow("4500", "F-2026-0500", null, null, "2", "Frais acheteur", 8.33, 1.67, "5", "ligne#2"),

            // Avoir 4600 lettré au bordereau d'origine 4500 (colonnes origin_* renseignées par l'auto-jointure).
            CreditNoteRow("4600", "AV-2026-0007", "4500", "4500", "F-2026-0500", new DateTime(2026, 1, 12), "4", "Annulation adjudication lot 9", 100.00, 20.00, "5", "ligne#1"),
            CreditNoteRow("4600", "AV-2026-0007", "4500", "4500", "F-2026-0500", new DateTime(2026, 1, 12), "2", "Annulation frais acheteur", 8.33, 1.67, "5", "ligne#2"),
        };
    }

    private static Dictionary<string, object?> CreditNoteRow(
        string noBa,
        string numeroPiece,
        string lettrage,
        string? originNoBa,
        string? originNumeroPiece,
        DateTime? originDateVente,
        string typeLigne,
        string designation,
        double montantHt,
        double montantTva,
        string codeRegime,
        string noLigne)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [EncheresV6Schema.ColNoBa] = noBa,
            [EncheresV6Schema.ColNumeroPiece] = numeroPiece,
            [EncheresV6Schema.ColBordereauOuAvoir] = "A",
            [EncheresV6Schema.ColDateVente] = new DateTime(2026, 2, 2),
            [EncheresV6Schema.ColNoBaLettrage] = lettrage,
            [EncheresV6Schema.ColAcheteurNom] = "Acheteur Particulier (fictif)",
            [EncheresV6Schema.ColSociete] = null,
            [EncheresV6Schema.ColAcheteurSiren] = null,
            [EncheresV6Schema.ColAcheteurVille] = "Rennes",
            [EncheresV6Schema.ColAcheteurCodePostal] = "35000",
            [EncheresV6Schema.ColAcheteurPays] = "FR",
            [EncheresV6Schema.ColTotalHt] = 108.33,
            [EncheresV6Schema.ColTotalTva] = 21.67,
            [EncheresV6Schema.ColTotalBordereau] = 130.00,
            [EncheresV6Schema.ColOriginNoBa] = originNoBa,
            [EncheresV6Schema.ColOriginNumeroPiece] = originNumeroPiece,
            [EncheresV6Schema.ColOriginDateVente] = originDateVente,
            [EncheresV6Schema.ColTypeLigne] = typeLigne,
            [EncheresV6Schema.ColDesignation] = designation,
            [EncheresV6Schema.ColMontantHt] = montantHt,
            [EncheresV6Schema.ColMontantTva] = montantTva,
            [EncheresV6Schema.ColTauxTva] = 20.0,
            [EncheresV6Schema.ColQuantite] = 1.0,
            [EncheresV6Schema.ColPrixUnitaire] = null,
            [EncheresV6Schema.ColCodeRegime] = codeRegime,
            [EncheresV6Schema.ColNoLigne] = noLigne,
        };
    }

    private static Dictionary<string, object?>[] OnePayment()
    {
        return new[] { PaymentRow("4500", "F-2026-0500", "ligne#3", 130.00, new DateTime(2026, 1, 15), "CB", "REM-0500") };
    }

    private static Dictionary<string, object?> PaymentRow(
        string noBa,
        string numeroPiece,
        string noLigne,
        double montantHt,
        DateTime dateReglement,
        string mode,
        string noRemise)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [EncheresV6Schema.ColNoBa] = noBa,
            [EncheresV6Schema.ColNumeroPiece] = numeroPiece,
            [EncheresV6Schema.ColNoLigne] = noLigne,
            [EncheresV6Schema.ColMontantLigne] = montantHt,
            [EncheresV6Schema.ColDateReglement] = dateReglement,
            [EncheresV6Schema.ColModeReglement] = mode,
            [EncheresV6Schema.ColNoRemise] = noRemise,
        };
    }

    // Un SEUL jeu de données (vente 4500 + son règlement type 3, avoir 4600 lettré à 4500) dérive les deux
    // chemins du test de parité : sérialisé en JSON pour le FixtureExtractor, converti en lignes de lecteur
    // pour le PervasiveExtractor (ODBC mocké). Mêmes valeurs source → même pivot attendu.
    private static EncheresV6SourceSnapshot ParityDataset()
    {
        var snapshot = new EncheresV6SourceSnapshot();

        var sale = new EncheresV6Bordereau
        {
            NoBa = "4500",
            NumeroPiece = "F-2026-0500",
            BordereauOuAvoir = "B",
            DateVente = new DateTime(2026, 1, 12),
            AcheteurNom = "Acheteur Particulier (fictif)",
            AcheteurVille = "Rennes",
            AcheteurCodePostal = "35000",
            AcheteurPays = "FR",
            TotalHt = 108.33,
            TotalTva = 21.67,
            TotalTtc = 130.00,
        };
        sale.Lignes.Add(new EncheresV6Ligne { TypeLigne = "4", Designation = "Adjudication lot 9", MontantHt = 100.00, MontantTva = 20.00, TauxTva = 20.0, Quantite = 1.0, PrixUnitaire = 100.00, CodeRegime = "5", NoLigne = "ligne#1" });
        sale.Lignes.Add(new EncheresV6Ligne { TypeLigne = "2", Designation = "Frais acheteur", MontantHt = 8.33, MontantTva = 1.67, TauxTva = 20.0, Quantite = 1.0, CodeRegime = "5", NoLigne = "ligne#2" });
        sale.Lignes.Add(new EncheresV6Ligne { TypeLigne = "3", Designation = "Reglement CB", MontantHt = 130.00, MontantTva = 0.0, NoLigne = "ligne#3", DateReglement = new DateTime(2026, 1, 15), ModeReglement = "CB", NoRemise = "REM-0500" });

        var avoir = new EncheresV6Bordereau
        {
            NoBa = "4600",
            NumeroPiece = "AV-2026-0007",
            BordereauOuAvoir = "A",
            DateVente = new DateTime(2026, 2, 2),
            NoBaLettrage = "4500",
            AcheteurNom = "Acheteur Particulier (fictif)",
            AcheteurVille = "Rennes",
            AcheteurCodePostal = "35000",
            AcheteurPays = "FR",
            TotalHt = 108.33,
            TotalTva = 21.67,
            TotalTtc = 130.00,
        };
        avoir.Lignes.Add(new EncheresV6Ligne { TypeLigne = "4", Designation = "Annulation adjudication lot 9", MontantHt = 100.00, MontantTva = 20.00, TauxTva = 20.0, Quantite = 1.0, PrixUnitaire = 100.00, CodeRegime = "5", NoLigne = "ligne#1" });
        avoir.Lignes.Add(new EncheresV6Ligne { TypeLigne = "2", Designation = "Annulation frais acheteur", MontantHt = 8.33, MontantTva = 1.67, TauxTva = 20.0, Quantite = 1.0, CodeRegime = "5", NoLigne = "ligne#2" });

        snapshot.Bordereaux.Add(sale);
        snapshot.Bordereaux.Add(avoir);
        return snapshot;
    }

    private static RecordingConnection ParityConnection(EncheresV6SourceSnapshot dataset)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> documentRows = ToDocumentRows(dataset);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> paymentRows = ToPaymentRows(dataset);

        return new RecordingConnection(readerResolver: sql =>
            sql == EncheresV6Schema.SelectPaymentsSql ? paymentRows
            : sql == EncheresV6Schema.SelectDocumentsSql ? documentRows
            : Array.Empty<IReadOnlyDictionary<string, object?>>());
    }

    // Projette le jeu de données comme la requête documents (ventes + avoirs, lignes type 4/2, entête d'origine
    // d'un avoir en colonnes origin_*). Les règlements (type 3) sont exclus — ils relèvent de ToPaymentRows.
    private static List<IReadOnlyDictionary<string, object?>> ToDocumentRows(EncheresV6SourceSnapshot dataset)
    {
        Dictionary<string, EncheresV6Bordereau> byNoBa = dataset.Bordereaux.ToDictionary(b => b.NoBa!, b => b, StringComparer.Ordinal);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (EncheresV6Bordereau b in dataset.Bordereaux.OrderBy(b => b.NoBa, StringComparer.Ordinal))
        {
            EncheresV6Bordereau? origin = null;
            if (string.Equals(b.BordereauOuAvoir, "A", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(b.NoBaLettrage))
            {
                byNoBa.TryGetValue(b.NoBaLettrage!, out origin);
            }

            foreach (EncheresV6Ligne line in b.Lignes.Where(l => l.TypeLigne == "4" || l.TypeLigne == "2"))
            {
                rows.Add(DocumentRow(b, origin, line));
            }
        }

        return rows;
    }

    private static Dictionary<string, object?> DocumentRow(EncheresV6Bordereau b, EncheresV6Bordereau? origin, EncheresV6Ligne line)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [EncheresV6Schema.ColNoBa] = b.NoBa,
            [EncheresV6Schema.ColNumeroPiece] = b.NumeroPiece,
            [EncheresV6Schema.ColBordereauOuAvoir] = b.BordereauOuAvoir,
            [EncheresV6Schema.ColDateVente] = b.DateVente,
            [EncheresV6Schema.ColNoBaLettrage] = b.NoBaLettrage,
            [EncheresV6Schema.ColAcheteurNom] = b.AcheteurNom,
            [EncheresV6Schema.ColSociete] = b.AcheteurSociete,
            [EncheresV6Schema.ColAcheteurSiren] = b.AcheteurSiren,
            [EncheresV6Schema.ColAcheteurVille] = b.AcheteurVille,
            [EncheresV6Schema.ColAcheteurCodePostal] = b.AcheteurCodePostal,
            [EncheresV6Schema.ColAcheteurPays] = b.AcheteurPays,
            [EncheresV6Schema.ColTotalHt] = b.TotalHt,
            [EncheresV6Schema.ColTotalTva] = b.TotalTva,
            [EncheresV6Schema.ColTotalBordereau] = b.TotalTtc,
            [EncheresV6Schema.ColOriginNoBa] = origin?.NoBa,
            [EncheresV6Schema.ColOriginNumeroPiece] = origin?.NumeroPiece,
            [EncheresV6Schema.ColOriginDateVente] = origin is null ? (object?)null : origin.DateVente,
            [EncheresV6Schema.ColTypeLigne] = line.TypeLigne,
            [EncheresV6Schema.ColDesignation] = line.Designation,
            [EncheresV6Schema.ColMontantHt] = line.MontantHt,
            [EncheresV6Schema.ColMontantTva] = line.MontantTva,
            [EncheresV6Schema.ColTauxTva] = line.TauxTva,
            [EncheresV6Schema.ColQuantite] = line.Quantite,
            [EncheresV6Schema.ColPrixUnitaire] = line.PrixUnitaire,
            [EncheresV6Schema.ColCodeRegime] = line.CodeRegime,
            [EncheresV6Schema.ColNoLigne] = line.NoLigne,
        };
    }

    private static List<IReadOnlyDictionary<string, object?>> ToPaymentRows(EncheresV6SourceSnapshot dataset)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (EncheresV6Bordereau b in dataset.Bordereaux.OrderBy(b => b.NoBa, StringComparer.Ordinal))
        {
            foreach (EncheresV6Ligne line in b.Lignes.Where(l => l.TypeLigne == "3"))
            {
                rows.Add(PaymentRow(b.NoBa!, b.NumeroPiece!, line.NoLigne!, line.MontantHt, line.DateReglement!.Value, line.ModeReglement!, line.NoRemise!));
            }
        }

        return rows;
    }
}

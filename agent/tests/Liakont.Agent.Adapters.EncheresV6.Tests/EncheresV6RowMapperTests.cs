namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Tests de la transformation BRUTE EncheresV6 → pivot (<see cref="EncheresV6RowMapper"/>),
/// partagée avec le futur PervasiveExtractor (ADP02). Accès aux types internes via
/// <c>InternalsVisibleTo</c>. Vérifie les invariants du contrat d'extraction : conversion
/// flottant→decimal half-up (CLAUDE.md n°1), aucun mapping TVA (R3), aucune règle inventée pour
/// la nature d'opération (CLAUDE.md n°2), avoir orphelin bloqué (ADR-0004 D3-3).
/// </summary>
public class EncheresV6RowMapperTests
{
    [Theory]
    [InlineData(8.329999999999998, 8.33)] // flottant Pervasive « sale » → nettoyé puis arrondi
    [InlineData(1.666, 1.67)]
    [InlineData(2.675, 2.68)] // demi-centime → half-up (away-from-zero)
    [InlineData(10.125, 10.13)]
    [InlineData(100.0, 100.00)]
    public void RoundAmount_converts_dirty_float_half_up(double raw, decimal expected)
    {
        EncheresV6RowMapper.RoundAmount(raw).Should().Be(expected);
    }

    [Fact]
    public void MapDocument_carries_operation_category_from_config_never_derives_it()
    {
        EncheresV6Bordereau bordereau = SaleWithAdjudicationAndFees();

        // La même donnée source, deux paramétrages : la nature suit la config, jamais une règle dérivée.
        PivotDocumentDto asGoods = EncheresV6RowMapper.MapDocument(bordereau, Emitter(), OperationCategory.LivraisonBiens, null);
        PivotDocumentDto asMixed = EncheresV6RowMapper.MapDocument(bordereau, Emitter(), OperationCategory.Mixte, null);

        asGoods.OperationCategory.Should().Be(OperationCategory.LivraisonBiens);
        asMixed.OperationCategory.Should().Be(OperationCategory.Mixte);
    }

    [Fact]
    public void MapDocument_keeps_source_regime_raw_and_leaves_tva_mapping_null()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapDocument(SaleWithAdjudicationAndFees(), Emitter(), OperationCategory.LivraisonBiens, null);

        PivotLineDto adjudication = doc.Lines[0];
        adjudication.SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("5");
        adjudication.Taxes.Should().ContainSingle();
        adjudication.Taxes[0].CategoryCode.Should().BeNull("le mapping TVA est plateforme (R3)");
        adjudication.Taxes[0].VatexCode.Should().BeNull();
        adjudication.Taxes[0].Rate.Should().Be(20m);
        adjudication.Taxes[0].TaxAmount.Should().Be(20.00m);
    }

    [Fact]
    public void MapDocument_carries_raw_document_kind_and_emitter_siren_from_config()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapDocument(SaleWithAdjudicationAndFees(), Emitter(), OperationCategory.LivraisonBiens, null);

        doc.SourceDocumentKind.Should().Be("B", "le type de pièce source est transporté brut (ADR-0004 D3-3)");
        doc.Supplier.Siren.Should().Be("111111111", "le SIREN émetteur vient de la config, pas de la base");
        doc.SourceReference.Should().Be("no_ba=4500");
        doc.CurrencyCode.Should().Be("EUR");
    }

    [Fact]
    public void MapDocument_rounds_dirty_float_and_keeps_original_in_source_data()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapDocument(SaleWithAdjudicationAndFees(), Emitter(), OperationCategory.LivraisonBiens, null);

        PivotLineDto fees = doc.Lines[1];
        fees.NetAmount.Should().Be(8.33m);
        fees.Taxes[0].TaxAmount.Should().Be(1.67m);
        fees.SourceData.Should().NotBeNull();
        fees.SourceData!.Should().Contain("8.329999999999998", "le montant original non arrondi est tracé (F01-F02 §3.7)");
    }

    [Fact]
    public void MapDocument_sets_company_hint_from_societe_without_heuristic()
    {
        PivotDocumentDto particulier = EncheresV6RowMapper.MapDocument(SaleWithAdjudicationAndFees(), Emitter(), OperationCategory.LivraisonBiens, null);
        particulier.Customer!.IsCompanyHint.Should().BeFalse();

        EncheresV6Bordereau pro = SaleWithAdjudicationAndFees();
        pro.AcheteurSociete = "Galerie Cliente SARL (fictif)";
        PivotDocumentDto professionnel = EncheresV6RowMapper.MapDocument(pro, Emitter(), OperationCategory.LivraisonBiens, null);
        professionnel.Customer!.IsCompanyHint.Should().BeTrue();
    }

    [Fact]
    public void MapDocument_excludes_payment_lines_from_document_lines()
    {
        EncheresV6Bordereau bordereau = SaleWithAdjudicationAndFees();
        bordereau.Lignes.Add(new EncheresV6Ligne
        {
            TypeLigne = "3",
            Designation = "Reglement CB",
            MontantHt = 130.00,
            DateReglement = new DateTime(2026, 1, 15),
            ModeReglement = "CB",
            NoLigne = "ligne#3",
        });

        PivotDocumentDto doc = EncheresV6RowMapper.MapDocument(bordereau, Emitter(), OperationCategory.LivraisonBiens, null);

        doc.Lines.Should().HaveCount(2, "les lignes de règlement (type 3) ne sont pas des lignes de document");
        doc.Payments.Should().BeEmpty("les encaissements passent par ExtractPayments, pas par le document");
    }

    [Fact]
    public void MapDocument_orphan_credit_note_is_blocked_never_guessed()
    {
        EncheresV6Bordereau avoir = SaleWithAdjudicationAndFees();
        avoir.BordereauOuAvoir = "A";
        avoir.NoBaLettrage = "9999";

        Action act = () => EncheresV6RowMapper.MapDocument(avoir, Emitter(), OperationCategory.LivraisonBiens, null);

        act.Should().Throw<SourceSchemaException>("un avoir sans origine résoluble est bloqué (ADR-0004 D3-3)");
    }

    [Fact]
    public void MapDocument_credit_note_links_to_resolved_origin()
    {
        EncheresV6Bordereau origin = SaleWithAdjudicationAndFees();
        EncheresV6Bordereau avoir = SaleWithAdjudicationAndFees();
        avoir.NoBa = "4600";
        avoir.NumeroPiece = "AV-2026-0007";
        avoir.BordereauOuAvoir = "A";
        avoir.NoBaLettrage = "4500";

        PivotDocumentDto doc = EncheresV6RowMapper.MapDocument(avoir, Emitter(), OperationCategory.LivraisonBiens, origin);

        doc.SourceDocumentKind.Should().Be("A");
        doc.CreditNoteRefs.Should().ContainSingle();
        doc.CreditNoteRefs[0].Number.Should().Be("F-2026-0500");
        doc.CreditNoteRefs[0].IssueDate.Should().Be(new DateTime(2026, 1, 12));
        doc.CreditNoteRefs[0].SourceReference.Should().Be("no_ba=4500");
    }

    [Fact]
    public void MapDocument_missing_required_field_is_a_schema_error()
    {
        EncheresV6Bordereau bordereau = SaleWithAdjudicationAndFees();
        bordereau.NumeroPiece = null;

        Action act = () => EncheresV6RowMapper.MapDocument(bordereau, Emitter(), OperationCategory.LivraisonBiens, null);

        act.Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void RoundAmount_throws_schema_exception_on_NaN_or_infinity()
    {
        Action actNaN = () => EncheresV6RowMapper.RoundAmount(double.NaN);
        Action actInfPos = () => EncheresV6RowMapper.RoundAmount(double.PositiveInfinity);

        actNaN.Should().Throw<SourceSchemaException>("un flottant NaN ne doit jamais être arrondi à l'aveugle (ADR-0004 D3-7)");
        actInfPos.Should().Throw<SourceSchemaException>("un flottant infini ne doit jamais être arrondi à l'aveugle (ADR-0004 D3-7)");
    }

    [Fact]
    public void MapDocument_throws_schema_exception_when_date_vente_is_default()
    {
        EncheresV6Bordereau bordereau = SaleWithAdjudicationAndFees();
        bordereau.DateVente = default;

        Action act = () => EncheresV6RowMapper.MapDocument(bordereau, Emitter(), OperationCategory.LivraisonBiens, null);

        act.Should().Throw<SourceSchemaException>("une date_vente manquante doit bloquer le document, jamais le laisser passer silencieusement");
    }

    [Fact]
    public void RoundAmount_throws_schema_exception_on_overflow()
    {
        // 1e30 > decimal.MaxValue (≈ 7.9e28) → OverflowException sur le cast → SourceSchemaException typée
        Action act = () => EncheresV6RowMapper.RoundAmount(1e30);

        act.Should().Throw<SourceSchemaException>("un montant hors de la plage decimal est bloqué (ADR-0004 D3-7)");
    }

    [Fact]
    public void SanitizeNonAmount_throws_schema_exception_on_overflow()
    {
        // 1e30 > decimal.MaxValue → OverflowException sur le cast → SourceSchemaException typée
        Action act = () => EncheresV6RowMapper.SanitizeNonAmount(1e30, "taux_tva");

        act.Should().Throw<SourceSchemaException>("une valeur non-montant hors de la plage decimal est bloquée (ADR-0004 D3-7)");
    }

    [Fact]
    public void MapPayment_maps_type3_line_to_raw_pivot_payment()
    {
        EncheresV6Bordereau bordereau = SaleWithAdjudicationAndFees();
        var reglement = new EncheresV6Ligne
        {
            TypeLigne = "3",
            Designation = "Reglement CB",
            MontantHt = 130.00,
            DateReglement = new DateTime(2026, 1, 15),
            ModeReglement = "CB",
            NoRemise = "REM-0500",
            NoLigne = "ligne#3",
        };

        PivotPaymentDto payment = EncheresV6RowMapper.MapPayment(bordereau, reglement);

        payment.PaymentDate.Should().Be(new DateTime(2026, 1, 15));
        payment.Amount.Should().Be(130.00m);
        payment.Method.Should().Be("CB");
        payment.RelatedDocumentNumber.Should().Be("F-2026-0500");
        payment.SourceReference.Should().Be("no_remise=REM-0500");
    }

    private static EncheresV6Bordereau SaleWithAdjudicationAndFees()
    {
        var bordereau = new EncheresV6Bordereau
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

        bordereau.Lignes.Add(new EncheresV6Ligne
        {
            TypeLigne = "4",
            Designation = "Adjudication lot 9",
            MontantHt = 100.00,
            MontantTva = 20.00,
            TauxTva = 20.0,
            Quantite = 1,
            PrixUnitaire = 100.00,
            CodeRegime = "5",
            NoLigne = "ligne#1",
        });

        bordereau.Lignes.Add(new EncheresV6Ligne
        {
            TypeLigne = "2",
            Designation = "Frais acheteur",
            MontantHt = 8.329999999999998,
            MontantTva = 1.666,
            TauxTva = 20.0,
            Quantite = 1,
            CodeRegime = "5",
            NoLigne = "ligne#2",
        });

        return bordereau;
    }

    private static EncheresV6EmitterIdentity Emitter() =>
        new EncheresV6EmitterIdentity(
            name: "Étude Fictïve SVV",
            siren: "111111111",
            city: "Rennes",
            postalCode: "35000",
            countryCode: "FR");
}

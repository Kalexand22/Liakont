namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Tests de la transformation BRUTE EncheresV6 → pivot (<see cref="EncheresV6RowMapper"/>), modèle BA/BV
/// de la marge (validé + sourcé). Vérifie les invariants du contrat d'extraction : commission acheteur en
/// <c>BuyerFees</c> TTC (BA, lignes type 1), commission vendeur en <c>SellerFees</c> TTC (BV, lignes type
/// 2), adjudication = seule LIGNE du document, émetteur/nature d'opération NON portés (FilledByPlatform),
/// conversion flottant→decimal half-up (CLAUDE.md n°1), aucun mapping TVA (R3), avoir orphelin bloqué.
/// </summary>
public class EncheresV6RowMapperTests
{
    [Theory]
    [InlineData(8.329999999999998, 8.33)] // flottant Pervasive « sale » → nettoyé puis arrondi
    [InlineData(1.666, 1.67)]
    [InlineData(2.675, 2.68)] // demi-centime → half-up (away-from-zero)
    [InlineData(100.0, 100.00)]
    public void RoundAmount_converts_dirty_float_half_up(double raw, decimal expected)
    {
        EncheresV6RowMapper.RoundAmount(raw).Should().Be(expected);
    }

    [Fact]
    public void RoundAmount_throws_schema_exception_on_NaN_infinity_or_overflow()
    {
        ((Action)(() => EncheresV6RowMapper.RoundAmount(double.NaN))).Should().Throw<SourceSchemaException>();
        ((Action)(() => EncheresV6RowMapper.RoundAmount(double.PositiveInfinity))).Should().Throw<SourceSchemaException>();
        ((Action)(() => EncheresV6RowMapper.RoundAmount(1e30))).Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void MapBaDocument_does_not_carry_supplier_or_operation_category()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapBaDocument(MargeBa(), null);

        doc.Supplier.Should().BeNull("l'émetteur est rempli par la plateforme (FilledByPlatform), jamais par l'agent");
        doc.OperationCategory.Should().BeNull("la nature d'opération est plateforme, jamais devinée (CLAUDE.md n°2)");
        doc.SourceDocumentKind.Should().Be("B", "le type de pièce source est transporté brut (ADR-0004 D3-3)");
        doc.SourceReference.Should().Be("encheresv6:ba:100022");
        doc.Number.Should().Be("100022");
    }

    [Fact]
    public void MapBaDocument_carries_buyer_commission_as_ttc_fee_not_a_taxable_line()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapBaDocument(MargeBa(), null);

        // Adjudication = seule LIGNE (régime marge → TVA 0 → document marge propre, art. 297 E).
        doc.Lines.Should().ContainSingle();
        doc.Lines[0].NetAmount.Should().Be(2000.00m);
        doc.Lines[0].Taxes[0].TaxAmount.Should().Be(0m, "sous le régime de la marge l'adjudication est exonérée");
        doc.Lines[0].Taxes[0].CategoryCode.Should().BeNull("le mapping TVA est plateforme (R3)");
        doc.Lines[0].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("6");
        doc.Totals.TotalTax.Should().Be(0m);

        // Commission acheteur = BuyerFee TTC (HT 334.40 + TVA 66.88), jamais une ligne taxable.
        doc.BuyerFees.Should().ContainSingle();
        doc.BuyerFees![0].NetAmount.Should().Be(401.28m);
        doc.BuyerFees[0].LotReference.Should().Be("100022", "la jambe acheteur est au grain bordereau (no_ba)");
        doc.SellerFees.Should().BeNull("le BA ne porte pas la jambe vendeur");
    }

    [Fact]
    public void MapBaDocument_reads_taxable_adjudication_vat_from_either_column()
    {
        EncheresV6Bordereau ba = MargeBa();
        ba.Lignes[0].CodeRegime = "5"; // taxable
        ba.Lignes[0].MttTvaInclusAdj = 400.00; // TVA d'adjudication « incluse »
        ba.Lignes[0].MttTvaEnPlusAdj = 0.0;

        PivotDocumentDto doc = EncheresV6RowMapper.MapBaDocument(ba, null);

        doc.Lines[0].Taxes[0].TaxAmount.Should().Be(400.00m);
        doc.Totals.TotalTax.Should().Be(400.00m);
    }

    [Fact]
    public void MapBaDocument_normalizes_source_currency_label_EURO_to_iso_EUR()
    {
        // La source EncheresV6 étiquette l'euro « EURO » (libellé non-ISO) : l'agent le normalise vers l'ISO 4217
        // « EUR » (sinon la plateforme BLOQUE le document — code devise invalide). Normalisation de FORMAT, pas fiscale.
        EncheresV6Bordereau euro = MargeBa();
        euro.CodeDevise = "EURO";
        EncheresV6RowMapper.MapBaDocument(euro, null).CurrencyCode.Should().Be("EUR");

        EncheresV6Bordereau vide = MargeBa();
        vide.CodeDevise = string.Empty;
        EncheresV6RowMapper.MapBaDocument(vide, null).CurrencyCode.Should().Be("EUR", "devise absente → EUR domestique");

        EncheresV6Bordereau iso = MargeBa();
        iso.CodeDevise = "USD";
        EncheresV6RowMapper.MapBaDocument(iso, null).CurrencyCode.Should().Be("USD", "un code ISO valide est transporté tel quel");
    }

    [Fact]
    public void MapBaDocument_sets_company_hint_and_siren_raw_without_heuristic()
    {
        PivotDocumentDto particulier = EncheresV6RowMapper.MapBaDocument(MargeBa(), null);
        particulier.Customer!.IsCompanyHint.Should().BeFalse();
        particulier.Customer.Siren.Should().BeNull();

        EncheresV6Bordereau pro = MargeBa();
        pro.Societe = "AUTOSUD21";
        pro.AcheteurSiren = "404482572";
        PivotDocumentDto professionnel = EncheresV6RowMapper.MapBaDocument(pro, null);
        professionnel.Customer!.IsCompanyHint.Should().BeTrue("le champ societe non vide est un indice brut");
        professionnel.Customer.Siren.Should().Be("404482572", "le SIREN est transporté brut, jamais déduit");
    }

    [Fact]
    public void MapBaDocument_orphan_credit_note_is_blocked_never_guessed()
    {
        EncheresV6Bordereau avoir = MargeBa();
        avoir.BordereauOuAvoir = "A";
        avoir.NoBaLettrage = "9999";

        Action act = () => EncheresV6RowMapper.MapBaDocument(avoir, null);

        act.Should().Throw<SourceSchemaException>("un avoir sans origine résoluble est bloqué (ADR-0004 D3-3)");
    }

    [Fact]
    public void MapBaDocument_credit_note_links_to_resolved_origin()
    {
        EncheresV6Bordereau origin = MargeBa();
        origin.NoBa = "100020";
        EncheresV6Bordereau avoir = MargeBa();
        avoir.NoBa = "100099";
        avoir.BordereauOuAvoir = "A";
        avoir.NoBaLettrage = "100020";

        PivotDocumentDto doc = EncheresV6RowMapper.MapBaDocument(avoir, origin);

        doc.SourceDocumentKind.Should().Be("A");
        doc.CreditNoteRefs.Should().ContainSingle();
        doc.CreditNoteRefs[0].Number.Should().Be("100020");
        doc.CreditNoteRefs[0].SourceReference.Should().Be("encheresv6:ba:100020");
    }

    [Fact]
    public void MapBaDocument_throws_when_date_vente_missing()
    {
        EncheresV6Bordereau ba = MargeBa();
        ba.DateVente = default;

        ((Action)(() => EncheresV6RowMapper.MapBaDocument(ba, null))).Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void MapBvDocument_carries_seller_commission_as_ttc_fee()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapBvDocument(MargeBv(), null);

        doc.Supplier.Should().BeNull();
        doc.Number.Should().Be("100012");
        doc.SourceReference.Should().Be("encheresv6:bv:100012");
        doc.Totals.TotalTax.Should().Be(0m, "la jambe vendeur ne porte pas de TVA distincte (marge)");

        // Commission vendeur = SellerFee TTC (HT 300 + TVA 60). Le débours (type 3) n'est PAS porté.
        doc.SellerFees.Should().ContainSingle();
        doc.SellerFees![0].NetAmount.Should().Be(360.00m);
        doc.SellerFees[0].LotReference.Should().Be("100012", "la jambe vendeur est au grain bordereau (no_bv)");
        doc.BuyerFees.Should().BeNull("le BV ne porte pas la jambe acheteur");
    }

    [Fact]
    public void MapBvDocument_excludes_debours_type3_from_margin()
    {
        EncheresV6BordereauVendeur bv = MargeBv();
        bv.Lignes.Add(new EncheresV6LigneVendeur
        {
            TypeLigne = "3", // débours (transport) — HORS marge
            Designation = "Transport",
            MttFraisHt = 100.00,
            MttTvaFrais = 20.00,
        });

        PivotDocumentDto doc = EncheresV6RowMapper.MapBvDocument(bv, null);

        doc.SellerFees.Should().ContainSingle("seule la commission (type 2) est portée ; le débours type 3 est hors marge");
        doc.SellerFees![0].NetAmount.Should().Be(360.00m);
    }

    [Fact]
    public void MapBvDocument_orphan_credit_note_is_blocked_never_guessed()
    {
        EncheresV6BordereauVendeur avoir = MargeBv();
        avoir.BordereauOuAvoir = "A";
        avoir.NoBvLettrage = "9999";

        Action act = () => EncheresV6RowMapper.MapBvDocument(avoir, null);

        act.Should().Throw<SourceSchemaException>("un avoir vendeur sans origine résoluble est bloqué (ADR-0004 D3-3)");
    }

    [Fact]
    public void MapPayment_maps_type3_line_to_raw_pivot_payment()
    {
        var bordereau = new EncheresV6Bordereau { NoBa = "100022" };
        var reglement = new EncheresV6Ligne
        {
            TypeLigne = "3",
            CodeLigne = "CB",
            Designation = "Carte bancaire",
            MontantLigne = 401.28,
            DateReglement = new DateTime(2024, 1, 15),
            NoRemise = "REM-0500",
            NoLignePv = "0",
        };

        PivotPaymentDto payment = EncheresV6RowMapper.MapPayment(bordereau, reglement);

        payment.PaymentDate.Should().Be(new DateTime(2024, 1, 15));
        payment.Amount.Should().Be(401.28m);
        payment.RelatedDocumentNumber.Should().Be("100022");
        payment.SourceReference.Should().Be("encheresv6:remise:REM-0500");
    }

    // Bordereau ACHETEUR : 1 lot au régime 6 (marge), adjudication 2000 (TVA 0) + commission acheteur
    // 334.40 HT + 66.88 TVA = 401.28 TTC (cas réel no_ba=100022).
    private static EncheresV6Bordereau MargeBa()
    {
        var ba = new EncheresV6Bordereau
        {
            NoBa = "100022",
            BordereauOuAvoir = "B",
            DateVente = new DateTime(2024, 1, 12),
            Nom = "Acheteur Particulier (fictif)",
            CodePostal = "35000",
            Ville = "Rennes",
            CodePays = "FR",
            TotalBordereau = 2401.28,
        };

        ba.Lignes.Add(new EncheresV6Ligne
        {
            TypeLigne = "1",
            NoLignePv = "1",
            Designation = "Adjudication lot 1",
            MontantAdjHt = 2000.00,
            MttTvaInclusAdj = 0.0,
            MttTvaEnPlusAdj = 0.0,
            MontantFraisHt = 334.40,
            MontantTvaFrais = 66.88,
            CodeRegime = "6",
        });

        return ba;
    }

    // Bordereau VENDEUR : 1 lot (adjudication) + commission vendeur 300 HT + 60 TVA = 360 TTC (type 2).
    private static EncheresV6BordereauVendeur MargeBv()
    {
        var bv = new EncheresV6BordereauVendeur
        {
            NoBv = "100012",
            BordereauOuAvoir = "B",
            DateVente = new DateTime(2024, 1, 12),
            Nom = "Vendeur Commettant (fictif)",
            CodeRegimeTva = "6",
            TotalBordereau = 1860.00,
        };

        bv.Lignes.Add(new EncheresV6LigneVendeur
        {
            TypeLigne = "1",
            NoLignePv = "1",
            Designation = "Adjudication lot 1",
            MontantAdjHt = 2000.00,
        });
        bv.Lignes.Add(new EncheresV6LigneVendeur
        {
            TypeLigne = "2",
            NoLignePv = "0",
            Designation = "Frais de vente 15%",
            MttFraisHt = 300.00,
            MttTvaFrais = 60.00,
        });

        return bv;
    }
}

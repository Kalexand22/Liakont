namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Xunit;

/// <summary>
/// Couvre la DÉRIVATION PURE du marqueur de déclaration de marge B2C (flux 10.3) posé par la plateforme
/// (<see cref="B2cMarginMarking.IsMarginDeclaration"/>), sur un pivot DÉJÀ enrichi par le mapping TVA validé.
/// Critère sourcé F03 : régime de la marge (E + VATEX-EU-F/I/J, §2.2/§2.3, table validée §3) + B2C (acheteur
/// non pro, §2.4) + frais (commission, §2.4) + 297 E (TVA distincte nulle, §2.3). Fail-closed sur chaque trou.
/// </summary>
public sealed class B2cMarginMarkingTests
{
    [Fact]
    public void MarginBuyerLeg_ParticulierAcheteur_IsMarked()
    {
        // Cas réel BA 100022 : adjudication exonérée (E + VATEX-EU-J), commission acheteur, acheteur particulier.
        var pivot = Pivot(
            lines: [MarginLine("VATEX-EU-J")],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeTrue();
    }

    [Fact]
    public void MarginSellerLeg_Commettant_IsMarked()
    {
        // Jambe vendeur (BV) : adjudication E + VATEX-EU-J, commission vendeur, commettant non assujetti (jamais de SIREN).
        var pivot = Pivot(
            lines: [MarginLine("VATEX-EU-J")],
            totalTax: 0m,
            customer: Commettant(),
            sellerFees: [SellerFee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeTrue();
    }

    [Fact]
    public void AnonymousBuyer_IsMarked_B2c()
    {
        // Acheteur anonyme (Customer null) = B2C particulier (cohérent BuyerLooksProfessionalRule).
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: null, buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeTrue();
    }

    [Theory]
    [InlineData("VATEX-EU-F")] // biens d'occasion
    [InlineData("VATEX-EU-I")] // œuvres d'art
    [InlineData("VATEX-EU-J")] // objets de collection
    public void AllMarginVatexCodes_AreAccepted(string vatex)
    {
        var pivot = Pivot([MarginLine(vatex)], totalTax: 0m, customer: Particulier(), buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeTrue();
    }

    [Fact]
    public void Taxable_WithSeparateVat_IsNotMarked_297E()
    {
        // Adjudication taxable (S, TVA distincte) → ce n'est pas une marge (art. 297 E), jamais marqué.
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["5"], taxes: [new PivotLineTaxDto(taxAmount: 400m, categoryCode: VatCategory.S)])],
            totalTax: 400m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithSiren_IsNotMarked_B2b()
    {
        // Acheteur identifié par un SIREN = vente B2B (e-invoicing), jamais un e-reporting B2C de la marge.
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: ProfessionnelSiren(), buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithVatNumber_IsNotMarked_B2b()
    {
        var buyer = new PivotPartyDto("ACME GMBH", vatNumber: "DE123456789");
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: buyer, buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithCompanyHint_IsNotMarked_B2b()
    {
        var buyer = new PivotPartyDto("Brocante du Centre", isCompanyHint: true);
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: buyer, buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NoFees_IsNotMarked()
    {
        // Pas de commission (acheteur ni vendeur) → pas de marge à déclarer.
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: Particulier());

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void ExemptWithoutMarginVatex_IsNotMarked_HorsChampAmbiguity()
    {
        // E sans VATEX (cas ambigu « marge ou hors champ ? », F03 §3) → jamais deviné, jamais marqué.
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["6"], taxes: [new PivotLineTaxDto(taxAmount: 0m, categoryCode: VatCategory.E)])],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void ExemptWithNonMarginVatex_IsNotMarked()
    {
        // E + VATEX d'une autre exonération (franchise) → pas le régime de la marge.
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["F"], taxes: [new PivotLineTaxDto(taxAmount: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-FR-FRANCHISE")])],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void MixedLines_OneTaxable_IsNotMarked_FailClosed()
    {
        // Une ligne marge + une ligne taxable → pas une marge pure → non marqué (fail-closed).
        var pivot = Pivot(
            lines:
            [
                MarginLine("VATEX-EU-J"),
                new PivotLineDto("Lot taxable", 500m, sourceRegimeCodes: ["5"], taxes: [new PivotLineTaxDto(taxAmount: 100m, categoryCode: VatCategory.S)]),
            ],
            totalTax: 100m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NoLines_IsNotMarked()
    {
        var pivot = Pivot([], totalTax: 0m, customer: Particulier(), buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NullPivot_IsNotMarked()
    {
        B2cMarginMarking.IsMarginDeclaration(null!).Should().BeFalse();
    }

    [Fact]
    public void LooksLikeUnclassifiedMargin_ExemptNonMarginVatex_WithFees_IsTrue()
    {
        // Forme marge (frais + exonéré, TVA=0) mais VATEX non-marge (franchise) → ambigu (F03 §6 #1) : à bloquer.
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["F"], taxes: [new PivotLineTaxDto(taxAmount: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-FR-FRANCHISE")])],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cMarginMarking.LooksLikeUnclassifiedMargin(pivot).Should().BeTrue();
    }

    [Fact]
    public void LooksLikeUnclassifiedMargin_ExemptMargin_ButProfessionalBuyer_IsTrue()
    {
        // Adjudication marge (E + VATEX-EU-J) MAIS acheteur professionnel (SIREN) → non classé marge (B2C requis),
        // forme marge (frais + TVA=0) → à bloquer (jamais routé en facture qui perdrait les honoraires).
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: ProfessionnelSiren(), buyerFees: [Fee()]);

        B2cMarginMarking.LooksLikeUnclassifiedMargin(pivot).Should().BeTrue();
    }

    [Fact]
    public void LooksLikeUnclassifiedMargin_RealMargin_IsFalse()
    {
        // Une vraie marge B2C est CLASSÉE marge (marquée → déférée vers B4) : pas concernée par la garde.
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: Particulier(), buyerFees: [Fee()]);

        B2cMarginMarking.LooksLikeUnclassifiedMargin(pivot).Should().BeFalse();
    }

    [Fact]
    public void LooksLikeUnclassifiedMargin_TaxableWithFees_IsFalse()
    {
        // Document TAXABLE (TVA distincte > 0) : voie nominale, jamais bloqué par cette garde (la commission en
        // ligne taxable relève de l'adaptateur, hors de ce maillon).
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["5"], taxes: [new PivotLineTaxDto(taxAmount: 400m, categoryCode: VatCategory.S)])],
            totalTax: 400m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cMarginMarking.LooksLikeUnclassifiedMargin(pivot).Should().BeFalse();
    }

    [Fact]
    public void LooksLikeUnclassifiedMargin_NoFees_IsFalse()
    {
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: ProfessionnelSiren());

        B2cMarginMarking.LooksLikeUnclassifiedMargin(pivot).Should().BeFalse();
    }

    private static PivotLineDto MarginLine(string vatex) =>
        new(
            description: "Adjudication",
            netAmount: 2000m,
            sourceRegimeCodes: ["6"],
            taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: vatex)]);

    private static PivotBuyerFeeDto Fee() => new("100022", 401.28m, sourceRegimeCode: "6");

    private static PivotSellerFeeDto SellerFee() => new("100012", 360.00m, sourceRegimeCode: "6");

    private static PivotPartyDto Particulier() =>
        new("Acheteur Particulier", address: new PivotAddressDto(city: "Rennes", countryCode: "FR"));

    private static PivotPartyDto Commettant() => new("Vendeur Commettant");

    private static PivotPartyDto ProfessionnelSiren() => new("AUTOSUD21", siren: "945678902");

    private static PivotDocumentDto Pivot(
        IReadOnlyList<PivotLineDto> lines,
        decimal totalTax,
        PivotPartyDto? customer,
        IReadOnlyList<PivotBuyerFeeDto>? buyerFees = null,
        IReadOnlyList<PivotSellerFeeDto>? sellerFees = null) =>
        new(
            sourceDocumentKind: "B",
            number: "100022",
            issueDate: new DateTime(2024, 1, 12),
            sourceReference: "encheresv6:ba:100022",
            supplier: null,
            totals: new PivotTotalsDto(totalNet: 2000m, totalTax: totalTax, totalGross: 2000m + totalTax),
            operationCategory: null,
            customer: customer,
            lines: lines,
            sellerFees: sellerFees,
            buyerFees: buyerFees);
}

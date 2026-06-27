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
    public void LooksLikeUnclassifiedMargin_ExemptMargin_ButProfessionalBuyer_IsFalse()
    {
        // BUG-17 volet a (buyer-indépendant, F03 §2.10) : un régime CLASSÉ marge (E + VATEX-EU-J) avec un
        // acheteur PROFESSIONNEL (SIREN) n'est PLUS « marge non classée ». Le CONTENU fiscal vient du régime
        // (classé), le CANAL de l'acheteur : l'acheteur identifié est ROUTÉ en aval (SIREN → facture B2B
        // e-invoicing), jamais happé par cette garde. Le portage de l'honoraire EN LIGNE (volet b) lève la perte
        // d'honoraire de la voie document : router devient sûr. ANCIEN attendu : true (bloqué) ; NOUVEAU : false.
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: ProfessionnelSiren(), buyerFees: [Fee()]);

        B2cMarginMarking.LooksLikeUnclassifiedMargin(pivot).Should().BeFalse();
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

    [Fact]
    public void IsMarginRegime_ProfessionalBuyer_IsTrue_ButNotAB2cDeclaration()
    {
        // Point central du récap marge : le RÉGIME est buyer-INDÉPENDANT (F03 §2.10). Une vente-marge à acheteur
        // PROFESSIONNEL (SIREN) EST au régime de la marge (IsMarginRegime vrai → récap + TVA-marge à déclarer),
        // même si ce N'EST PAS un e-reporting B2C (IsMarginDeclaration faux → facture B2B « Régime particulier »).
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: ProfessionnelSiren(), buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginRegime(pivot).Should().BeTrue("le régime de la marge ne dépend pas de l'acheteur");
        B2cMarginMarking.IsMarginDeclaration(pivot).Should().BeFalse("un acheteur pro relève du B2B, pas de l'e-reporting B2C");
    }

    [Fact]
    public void IsMarginRegime_ParticulierBuyer_IsTrue()
    {
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: Particulier(), buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginRegime(pivot).Should().BeTrue();
    }

    [Fact]
    public void IsMarginRegime_TaxableWithSeparateVat_IsFalse_297E()
    {
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["5"], taxes: [new PivotLineTaxDto(taxAmount: 400m, categoryCode: VatCategory.S)])],
            totalTax: 400m,
            customer: ProfessionnelSiren(),
            buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginRegime(pivot).Should().BeFalse("TVA distincte → pas de marge (art. 297 E)");
    }

    [Fact]
    public void IsMarginRegime_NoFees_IsFalse()
    {
        var pivot = Pivot([MarginLine("VATEX-EU-J")], totalTax: 0m, customer: Particulier());

        B2cMarginMarking.IsMarginRegime(pivot).Should().BeFalse();
    }

    [Fact]
    public void IsMarginRegime_ExemptWithoutMarginVatex_IsFalse()
    {
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["6"], taxes: [new PivotLineTaxDto(taxAmount: 0m, categoryCode: VatCategory.E)])],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cMarginMarking.IsMarginRegime(pivot).Should().BeFalse("E sans VATEX de marge = ambigu (F03 §3), jamais deviné");
    }

    [Fact]
    public void IsMarginRegime_NullPivot_IsFalse()
    {
        B2cMarginMarking.IsMarginRegime(null!).Should().BeFalse();
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

    // BUG-17 volet b : l'honoraire acheteur est désormais porté en LIGNE (rôle BuyerFee), non plus dans le
    // side-channel hors-lignes BuyerFees. Le helper Pivot transcrit chaque Fee() en LIGNE BuyerFee : elle porte
    // la MÊME ventilation (catégorie/VATEX) que l'adjudication (le mapping plateforme classe l'honoraire au même
    // régime que le lot) avec une TVA de ligne nulle (art. 297 E). Seule la construction de l'entrée change ; les
    // RÉSULTATS attendus (marquage) sont inchangés.
    private static PivotLineDto BuyerFeeLine(IReadOnlyList<PivotLineDto> lines, PivotBuyerFeeDto fee)
    {
        var model = lines.Count > 0 ? lines[0].Taxes[0] : new PivotLineTaxDto(taxAmount: 0m, rate: null);
        return new PivotLineDto(
            description: "Honoraires acheteur",
            netAmount: fee.NetAmount,
            sourceRegimeCodes: lines.Count > 0 ? lines[0].SourceRegimeCodes : ["6"],
            taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: model.Rate, categoryCode: model.CategoryCode, vatexCode: model.VatexCode)],
            role: PivotLineRole.BuyerFee,
            sourceTaxAmount: fee.SourceTaxAmount);
    }

    private static PivotDocumentDto Pivot(
        IReadOnlyList<PivotLineDto> lines,
        decimal totalTax,
        PivotPartyDto? customer,
        IReadOnlyList<PivotBuyerFeeDto>? buyerFees = null,
        IReadOnlyList<PivotSellerFeeDto>? sellerFees = null)
    {
        var allLines = new List<PivotLineDto>(lines);
        foreach (var fee in buyerFees ?? [])
        {
            allLines.Add(BuyerFeeLine(lines, fee));
        }

        return new(
            sourceDocumentKind: "B",
            number: "100022",
            issueDate: new DateTime(2024, 1, 12),
            sourceReference: "encheresv6:ba:100022",
            supplier: null,
            totals: new PivotTotalsDto(totalNet: 2000m, totalTax: totalTax, totalGross: 2000m + totalTax),
            operationCategory: null,
            customer: customer,
            lines: allLines,
            sellerFees: sellerFees);
    }
}

namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Xunit;

/// <summary>
/// Couvre la DÉRIVATION PURE du marqueur de déclaration B2C TAXABLE au régime du prix total (flux 10.3, TLB1)
/// posé par la plateforme (<see cref="B2cTaxableMarking.IsTaxableB2cDeclaration"/>), sur un pivot DÉJÀ enrichi.
/// Critère sourcé F03 §2.7 : régime du prix total (TVA distincte > 0, toutes lignes S/AA/AAA) + frais d'enchères
/// + B2C (acheteur non pro). Fail-closed sur chaque trou. Symétrique de <c>B2cMarginMarkingTests</c>.
/// </summary>
public sealed class B2cTaxableMarkingTests
{
    [Fact]
    public void TaxableBuyerLeg_ParticulierAcheteur_IsMarked()
    {
        // Régime 5 : adjudication taxable (S, TVA distincte), commission acheteur, acheteur particulier.
        var pivot = Pivot(
            lines: [TaxableLine(VatCategory.S, net: 2000m, vat: 400m, rate: 20m)],
            totalTax: 400m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeTrue();
    }

    [Fact]
    public void AnonymousBuyer_IsMarked_B2c()
    {
        var pivot = Pivot([TaxableLine(VatCategory.S, 2000m, 400m, 20m)], totalTax: 400m, customer: null, buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeTrue();
    }

    [Theory]
    [InlineData(VatCategory.S)]
    [InlineData(VatCategory.AA)]
    [InlineData(VatCategory.AAA)]
    public void AllPositiveRateTaxableCategories_AreAccepted(VatCategory category)
    {
        var pivot = Pivot([TaxableLine(category, 2000m, 110m, 5.5m)], totalTax: 110m, customer: Particulier(), buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeTrue();
    }

    [Fact]
    public void Margin_NoSeparateVat_IsNotMarked()
    {
        // Marge (E + VATEX, art. 297 E, TotalTax == 0) → ce n'est PAS le régime du prix total (c'est TMA1).
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["6"], taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")])],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void TaxableButZeroTotalTax_IsNotMarked()
    {
        // Catégorie S mais aucune TVA distincte au grain document → fail-closed (pas un prix total taxable).
        var pivot = Pivot([TaxableLine(VatCategory.S, 2000m, 0m, 20m)], totalTax: 0m, customer: Particulier(), buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithSiren_IsNotMarked_B2b()
    {
        var pivot = Pivot([TaxableLine(VatCategory.S, 2000m, 400m, 20m)], totalTax: 400m, customer: ProfessionnelSiren(), buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithVatNumber_IsNotMarked_B2b()
    {
        var buyer = new PivotPartyDto("ACME GMBH", vatNumber: "DE123456789");
        var pivot = Pivot([TaxableLine(VatCategory.S, 2000m, 400m, 20m)], totalTax: 400m, customer: buyer, buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithCompanyHint_IsNotMarked_B2b()
    {
        var buyer = new PivotPartyDto("Brocante du Centre", isCompanyHint: true);
        var pivot = Pivot([TaxableLine(VatCategory.S, 2000m, 400m, 20m)], totalTax: 400m, customer: buyer, buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NoFees_IsNotMarked()
    {
        // Facture B2C taxable ORDINAIRE (sans frais d'enchères) → jamais happée vers le job agrégé.
        var pivot = Pivot([TaxableLine(VatCategory.S, 2000m, 400m, 20m)], totalTax: 400m, customer: Particulier());

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void MixedLines_OneExempt_IsNotMarked_FailClosed()
    {
        // Une ligne taxable + une ligne exonérée → pas un prix total taxable pur → non marqué (fail-closed).
        var pivot = Pivot(
            lines:
            [
                TaxableLine(VatCategory.S, 2000m, 400m, 20m),
                new PivotLineDto("Lot exonéré", 500m, sourceRegimeCodes: ["6"], taxes: [new PivotLineTaxDto(taxAmount: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")]),
            ],
            totalTax: 400m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void ZeroRatedCategory_IsNotMarked()
    {
        // Z (taux zéro) n'est pas une livraison « soumise à la TVA » au sens TLB1 → non marqué.
        var pivot = Pivot([new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["5"], taxes: [new PivotLineTaxDto(taxAmount: 0m, categoryCode: VatCategory.Z, rate: 0m)])], totalTax: 0m, customer: Particulier(), buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NoLines_IsNotMarked()
    {
        var pivot = Pivot([], totalTax: 400m, customer: Particulier(), buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NullPivot_IsNotMarked()
    {
        B2cTaxableMarking.IsTaxableB2cDeclaration(null!).Should().BeFalse();
    }

    [Fact]
    public void MarginAndTaxable_ArePartitionedByTotalTax()
    {
        // Invariant d'aiguillage : un document ne peut être marqué marge ET taxable (mutuellement exclusifs).
        var taxable = Pivot([TaxableLine(VatCategory.S, 2000m, 400m, 20m)], totalTax: 400m, customer: Particulier(), buyerFees: [Fee()]);
        var margin = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["6"], taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")])],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cTaxableMarking.IsTaxableB2cDeclaration(taxable).Should().BeTrue();
        B2cMarginMarking.IsMarginDeclaration(taxable).Should().BeFalse();
        B2cTaxableMarking.IsTaxableB2cDeclaration(margin).Should().BeFalse();
        B2cMarginMarking.IsMarginDeclaration(margin).Should().BeTrue();
    }

    private static PivotLineDto TaxableLine(VatCategory category, decimal net, decimal vat, decimal rate) =>
        new(
            description: "Adjudication",
            netAmount: net,
            sourceRegimeCodes: ["5"],
            taxes: [new PivotLineTaxDto(taxAmount: vat, rate: rate, categoryCode: category)]);

    private static PivotBuyerFeeDto Fee() => new("100050", 480.00m, sourceRegimeCode: "5");

    private static PivotPartyDto Particulier() =>
        new("Acheteur Particulier", address: new PivotAddressDto(city: "Rennes", countryCode: "FR"));

    private static PivotPartyDto ProfessionnelSiren() => new("AUTOSUD21", siren: "945678902");

    private static PivotDocumentDto Pivot(
        IReadOnlyList<PivotLineDto> lines,
        decimal totalTax,
        PivotPartyDto? customer,
        IReadOnlyList<PivotBuyerFeeDto>? buyerFees = null) =>
        new(
            sourceDocumentKind: "B",
            number: "100050",
            issueDate: new System.DateTime(2024, 1, 12),
            sourceReference: "encheresv6:ba:100050",
            supplier: null,
            totals: new PivotTotalsDto(totalNet: 2000m, totalTax: totalTax, totalGross: 2000m + totalTax),
            operationCategory: null,
            customer: customer,
            lines: lines,
            buyerFees: buyerFees);
}

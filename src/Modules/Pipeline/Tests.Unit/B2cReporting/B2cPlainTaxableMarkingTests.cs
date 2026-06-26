namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Xunit;

/// <summary>
/// Couvre la DÉRIVATION PURE du marqueur de déclaration B2C d'un document ORDINAIRE taxable (flux 10.3, TLB1/TPS1)
/// posé par la plateforme (<see cref="B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration"/>), sur un pivot DÉJÀ
/// enrichi. Critère sourcé F03 §2.9 : taxable (TVA distincte > 0, toutes lignes S/AA/AAA) + B2C (acheteur non pro)
/// + AUCUN frais d'enchères (discriminant « document ordinaire »). Fail-closed sur chaque trou. La PARTITION avec
/// <see cref="B2cTaxableMarking"/> (qui EXIGE des frais) est explicitement vérifiée.
/// </summary>
public sealed class B2cPlainTaxableMarkingTests
{
    [Fact]
    public void PlainTaxable_ParticulierAcheteur_NoFees_IsMarked()
    {
        // Facture client B2C taxable (S, TVA distincte), acheteur particulier, AUCUN frais d'enchères.
        var pivot = Pivot(
            lines: [TaxableLine(VatCategory.S, net: 1000m, vat: 200m, rate: 20m)],
            totalTax: 200m,
            customer: Particulier());

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeTrue();
    }

    [Fact]
    public void AnonymousBuyer_NoFees_IsMarked_B2c()
    {
        var pivot = Pivot([TaxableLine(VatCategory.S, 1000m, 200m, 20m)], totalTax: 200m, customer: null);

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeTrue();
    }

    [Theory]
    [InlineData(VatCategory.S)]
    [InlineData(VatCategory.AA)]
    [InlineData(VatCategory.AAA)]
    public void AllPositiveRateTaxableCategories_AreAccepted(VatCategory category)
    {
        var pivot = Pivot([TaxableLine(category, 1000m, 55m, 5.5m)], totalTax: 55m, customer: Particulier());

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeTrue();
    }

    [Fact]
    public void WithBuyerFees_IsNotMarked_ItIsAnAuctionBordereau()
    {
        // Présence de frais acheteur = bordereau d'enchères (B2cTaxableMarking), JAMAIS le document ordinaire.
        var pivot = Pivot([TaxableLine(VatCategory.S, 1000m, 200m, 20m)], totalTax: 200m, customer: Particulier(), buyerFees: [Fee()]);

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void WithSellerFees_IsNotMarked_ItIsAnAuctionBordereau()
    {
        var pivot = Pivot([TaxableLine(VatCategory.S, 1000m, 200m, 20m)], totalTax: 200m, customer: Particulier(), sellerFees: [SellerFee()]);

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void ZeroTotalTax_IsNotMarked_FailClosed()
    {
        // Catégorie S mais aucune TVA distincte → fail-closed (un document ordinaire taxable a une TVA > 0).
        var pivot = Pivot([TaxableLine(VatCategory.S, 1000m, 0m, 20m)], totalTax: 0m, customer: Particulier());

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void ExemptLine_IsNotMarked()
    {
        // Une ligne exonérée (E + VATEX) n'est pas un document ordinaire taxable → non marqué.
        var pivot = Pivot(
            lines: [new PivotLineDto("Prestation exonérée", 1000m, sourceRegimeCodes: ["EXO"], taxes: [new PivotLineTaxDto(taxAmount: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")])],
            totalTax: 0m,
            customer: Particulier());

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithSiren_IsNotMarked_B2b()
    {
        var pivot = Pivot([TaxableLine(VatCategory.S, 1000m, 200m, 20m)], totalTax: 200m, customer: ProfessionnelSiren());

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithVatNumber_IsNotMarked_B2b()
    {
        var buyer = new PivotPartyDto("ACME GMBH", vatNumber: "DE123456789");
        var pivot = Pivot([TaxableLine(VatCategory.S, 1000m, 200m, 20m)], totalTax: 200m, customer: buyer);

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void MixedLines_OneExempt_IsNotMarked_FailClosed()
    {
        var pivot = Pivot(
            lines:
            [
                TaxableLine(VatCategory.S, 1000m, 200m, 20m),
                new PivotLineDto("Ligne exonérée", 500m, sourceRegimeCodes: ["EXO"], taxes: [new PivotLineTaxDto(taxAmount: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")]),
            ],
            totalTax: 200m,
            customer: Particulier());

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NoLines_IsNotMarked()
    {
        var pivot = Pivot([], totalTax: 200m, customer: Particulier());

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NullPivot_IsNotMarked()
    {
        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(null!).Should().BeFalse();
    }

    [Fact]
    public void PlainAndAuctionTaxable_ArePartitionedByFees()
    {
        // Invariant d'aiguillage : SANS frais → ordinaire (Plain) ; AVEC frais → enchères (Taxable). Jamais les deux.
        var plain = Pivot([TaxableLine(VatCategory.S, 1000m, 200m, 20m)], totalTax: 200m, customer: Particulier());
        var auction = Pivot([TaxableLine(VatCategory.S, 1000m, 200m, 20m)], totalTax: 200m, customer: Particulier(), buyerFees: [Fee()]);

        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(plain).Should().BeTrue();
        B2cTaxableMarking.IsTaxableB2cDeclaration(plain).Should().BeFalse();
        B2cPlainTaxableMarking.IsPlainTaxableB2cDeclaration(auction).Should().BeFalse();
        B2cTaxableMarking.IsTaxableB2cDeclaration(auction).Should().BeTrue();
    }

    private static PivotLineDto TaxableLine(VatCategory category, decimal net, decimal vat, decimal rate) =>
        new(
            description: "Ligne taxable",
            netAmount: net,
            sourceRegimeCodes: ["TVA20"],
            taxes: [new PivotLineTaxDto(taxAmount: vat, rate: rate, categoryCode: category)]);

    private static PivotBuyerFeeDto Fee() => new("100050", 480.00m, sourceRegimeCode: "5");

    private static PivotSellerFeeDto SellerFee() => new("100050", 360.00m, sourceRegimeCode: "5");

    private static PivotPartyDto Particulier() =>
        new("Client Particulier", address: new PivotAddressDto(city: "Rennes", countryCode: "FR"));

    private static PivotPartyDto ProfessionnelSiren() => new("AUTOSUD21", siren: "945678902");

    private static PivotDocumentDto Pivot(
        IReadOnlyList<PivotLineDto> lines,
        decimal totalTax,
        PivotPartyDto? customer,
        IReadOnlyList<PivotBuyerFeeDto>? buyerFees = null,
        IReadOnlyList<PivotSellerFeeDto>? sellerFees = null) =>
        new(
            sourceDocumentKind: "F",
            number: "FC-100",
            issueDate: new System.DateTime(2024, 1, 12),
            sourceReference: "encheresv6:fc:100",
            supplier: null,
            totals: new PivotTotalsDto(totalNet: 1000m, totalTax: totalTax, totalGross: 1000m + totalTax),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: customer,
            lines: lines,
            sellerFees: sellerFees,
            buyerFees: buyerFees);
}

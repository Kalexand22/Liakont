namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Xunit;

/// <summary>
/// Couvre la DÉRIVATION PURE du marqueur de déclaration B2C d'EXPORT HORS UE détaxé (flux 10.3, TLB1 unitaire,
/// art. 262 I) posé par la plateforme (<see cref="B2cExportMarking.IsExportDeclaration"/>), sur un pivot DÉJÀ
/// enrichi. Critère sourcé F03 §2.8 : toutes lignes catégorie <c>G</c> + aucune TVA distincte + frais d'enchères
/// + B2C (acheteur non pro). Fail-closed sur chaque trou. Symétrique de <c>B2cTaxableMarkingTests</c> /
/// <c>B2cMarginMarkingTests</c>, avec en plus l'invariant de PARTITION à TROIS cas (marge / prix total / export).
/// </summary>
public sealed class B2cExportMarkingTests
{
    [Fact]
    public void ExportHorsUe_ParticulierAcheteur_IsMarked()
    {
        // Export hors UE : adjudication catégorie G (détaxée, 262 I), commission acheteur, acheteur particulier.
        var pivot = Pivot(
            lines: [ExportLine(net: 2000m)],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeTrue();
    }

    [Fact]
    public void AnonymousBuyer_IsMarked_B2c()
    {
        var pivot = Pivot([ExportLine(2000m)], totalTax: 0m, customer: null, buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeTrue();
    }

    [Fact]
    public void Margin_CategoryE_IsNotMarked_Export()
    {
        // Marge (E + VATEX, art. 297 E, TotalTax == 0) partage TotalTax == 0 mais N'EST PAS un export (catégorie E).
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["6"], taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")])],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void TaxableCategoryS_IsNotMarked_Export()
    {
        // Prix total taxable (S, TVA distincte) n'est pas un export détaxé.
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["5"], taxes: [new PivotLineTaxDto(taxAmount: 400m, rate: 20m, categoryCode: VatCategory.S)])],
            totalTax: 400m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void ExportLineWithResidualVat_IsNotMarked_FailClosed()
    {
        // Ligne catégorie G mais portant une TVA résiduelle (> 0) → incohérent avec un export détaxé → non marqué.
        var pivot = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["5_EXP_HORSUE"], taxes: [new PivotLineTaxDto(taxAmount: 5m, rate: 0m, categoryCode: VatCategory.G, vatexCode: "VATEX-EU-G")])],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void TotalTaxNonZero_IsNotMarked()
    {
        // Catégorie G mais TVA distincte au grain document → incohérent (un export est exonéré) → fail-closed.
        var pivot = Pivot([ExportLine(2000m)], totalTax: 10m, customer: Particulier(), buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithSiren_IsNotMarked_B2b()
    {
        var pivot = Pivot([ExportLine(2000m)], totalTax: 0m, customer: ProfessionnelSiren(), buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithVatNumber_IsNotMarked_B2b()
    {
        var buyer = new PivotPartyDto("ACME GMBH", vatNumber: "DE123456789");
        var pivot = Pivot([ExportLine(2000m)], totalTax: 0m, customer: buyer, buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void BuyerWithCompanyHint_IsNotMarked_B2b()
    {
        var buyer = new PivotPartyDto("Brocante du Centre", isCompanyHint: true);
        var pivot = Pivot([ExportLine(2000m)], totalTax: 0m, customer: buyer, buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NoFees_IsNotMarked()
    {
        // Facture B2C exonérée ORDINAIRE (sans frais d'enchères) → jamais happée vers le job d'export.
        var pivot = Pivot([ExportLine(2000m)], totalTax: 0m, customer: Particulier());

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void MixedLines_OneTaxable_IsNotMarked_FailClosed()
    {
        // Une ligne export (G) + une ligne taxable (S) → pas un export pur → non marqué (fail-closed).
        var pivot = Pivot(
            lines:
            [
                ExportLine(2000m),
                new PivotLineDto("Lot taxable", 500m, sourceRegimeCodes: ["5"], taxes: [new PivotLineTaxDto(taxAmount: 100m, rate: 20m, categoryCode: VatCategory.S)]),
            ],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NoLines_IsNotMarked()
    {
        var pivot = Pivot([], totalTax: 0m, customer: Particulier(), buyerFees: [Fee()]);

        B2cExportMarking.IsExportDeclaration(pivot).Should().BeFalse();
    }

    [Fact]
    public void NullPivot_IsNotMarked()
    {
        B2cExportMarking.IsExportDeclaration(null!).Should().BeFalse();
    }

    [Fact]
    public void Margin_Taxable_Export_ArePartitioned()
    {
        // Invariant d'aiguillage à TROIS cas : un document est AU PLUS l'un des trois (jamais happé par deux jobs).
        var export = Pivot([ExportLine(2000m)], totalTax: 0m, customer: Particulier(), buyerFees: [Fee()]);
        var taxable = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["5"], taxes: [new PivotLineTaxDto(taxAmount: 400m, rate: 20m, categoryCode: VatCategory.S)])],
            totalTax: 400m,
            customer: Particulier(),
            buyerFees: [Fee()]);
        var margin = Pivot(
            lines: [new PivotLineDto("Adjudication", 2000m, sourceRegimeCodes: ["6"], taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J")])],
            totalTax: 0m,
            customer: Particulier(),
            buyerFees: [Fee()]);

        // Export : marqué export, ni marge ni taxable.
        B2cExportMarking.IsExportDeclaration(export).Should().BeTrue();
        B2cMarginMarking.IsMarginDeclaration(export).Should().BeFalse();
        B2cTaxableMarking.IsTaxableB2cDeclaration(export).Should().BeFalse();

        // Marge : marquée marge, pas export.
        B2cMarginMarking.IsMarginDeclaration(margin).Should().BeTrue();
        B2cExportMarking.IsExportDeclaration(margin).Should().BeFalse();

        // Taxable : marqué taxable, pas export.
        B2cTaxableMarking.IsTaxableB2cDeclaration(taxable).Should().BeTrue();
        B2cExportMarking.IsExportDeclaration(taxable).Should().BeFalse();
    }

    private static PivotLineDto ExportLine(decimal net) =>
        new(
            description: "Adjudication (export hors UE)",
            netAmount: net,
            sourceRegimeCodes: ["5_EXP_HORSUE"],
            taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.G, vatexCode: "VATEX-EU-G")]);

    private static PivotBuyerFeeDto Fee() => new("100050", 480.00m, sourceRegimeCode: "5");

    private static PivotPartyDto Particulier() =>
        new("Acheteur Particulier", address: new PivotAddressDto(city: "Londres", countryCode: "GB"));

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

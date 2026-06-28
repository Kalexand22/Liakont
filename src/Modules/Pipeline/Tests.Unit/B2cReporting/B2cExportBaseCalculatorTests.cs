namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Xunit;

/// <summary>
/// Couvre le cœur PUR du calcul de la BASE HT d'un export hors UE détaxé
/// (<see cref="B2cExportBaseCalculator.ComputeTaxExclusiveBase"/>, F03 §2.8) : Σ adjudications HT + Σ commission
/// acheteur, commission vendeur EXCLUE. Sous un export la commission est exonérée (TTC = HT), aucun « ramené HT ».
/// </summary>
public sealed class B2cExportBaseCalculatorTests
{
    [Fact]
    public void Base_Is_Adjudication_Plus_Buyer_Commission()
    {
        // Cas nominal : adjudication 120 HT + commission acheteur 60 (exonérée → TTC = HT) = 180.
        var pivot = Pivot(
            lines: [Line(120m)],
            buyerFees: [BuyerFee(60m)]);

        B2cExportBaseCalculator.ComputeTaxExclusiveBase(pivot).Should().Be(180m);
    }

    [Fact]
    public void Base_Recovers_Tax_Exclusive_Commission_When_Source_Fee_Vat_Is_Carried()
    {
        // Guard P2 (F03 §2.8) : si la source portait une TVA de frais (commission NON détaxée), la base HT
        // doit la RETRANCHER — recouvrement par construction, sans s'appuyer sur l'invariant « TVA frais = 0 ».
        // Ici commission TTC 72 dont 12 de TVA source → HT 60 ; base = adjudication 120 + 60 = 180.
        var pivot = Pivot(
            lines: [Line(120m)],
            buyerFees: [BuyerFee(72m, sourceTaxAmount: 12m)]);

        B2cExportBaseCalculator.ComputeTaxExclusiveBase(pivot).Should().Be(180m);
    }

    [Fact]
    public void Base_Sums_All_Adjudication_Lines()
    {
        // Bordereau multi-lots : Σ des adjudications HT + commission acheteur.
        var pivot = Pivot(
            lines: [Line(120m), Line(80m)],
            buyerFees: [BuyerFee(50m)]);

        B2cExportBaseCalculator.ComputeTaxExclusiveBase(pivot).Should().Be(250m);
    }

    [Fact]
    public void Base_Excludes_Seller_Commission()
    {
        // La commission VENDEUR (jambe B2B) n'entre JAMAIS dans la base d'export (F03 §2.8) : seules les lignes
        // (adjudication + honoraire acheteur) comptent. Ici : 120 + 60 = 180, la commission vendeur 999 est ignorée.
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "BAX",
            issueDate: new System.DateTime(2026, 1, 20),
            sourceReference: "encheresv6:ba:1",
            supplier: null,
            totals: new PivotTotalsDto(120m, 0m, 120m),
            operationCategory: null,
            customer: null,
            lines: [Line(120m), BuyerFee(60m)],
            sellerFees: [new PivotSellerFeeDto("lot-7", 999m, sourceRegimeCode: "EXP_HORSUE")]);

        B2cExportBaseCalculator.ComputeTaxExclusiveBase(pivot).Should().Be(180m);
    }

    [Fact]
    public void Base_Without_Buyer_Fees_Is_Only_The_Adjudication()
    {
        var pivot = Pivot(lines: [Line(120m)], buyerFees: null);

        B2cExportBaseCalculator.ComputeTaxExclusiveBase(pivot).Should().Be(120m);
    }

    private static PivotLineDto Line(decimal net) =>
        new(
            description: "Adjudication (export hors UE)",
            netAmount: net,
            sourceRegimeCodes: ["EXP_HORSUE"],
            taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.G, vatexCode: "VATEX-EU-G")]);

    // BUG-17 volet b : l'honoraire acheteur est porté en LIGNE (rôle BuyerFee) — NetAmount TTC, TVA de frais
    // source à part (SourceTaxAmount). Le calculateur recouvre son HT (NetAmount − SourceTaxAmount), comme avant.
    private static PivotLineDto BuyerFee(decimal netTtc, decimal? sourceTaxAmount = null) =>
        new(
            description: "Honoraires acheteur (export hors UE)",
            netAmount: netTtc,
            sourceRegimeCodes: ["EXP_HORSUE"],
            taxes: [new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.G, vatexCode: "VATEX-EU-G")],
            role: PivotLineRole.BuyerFee,
            sourceTaxAmount: sourceTaxAmount);

    private static PivotDocumentDto Pivot(
        System.Collections.Generic.IReadOnlyList<PivotLineDto> lines,
        System.Collections.Generic.IReadOnlyList<PivotLineDto>? buyerFees) =>
        new(
            sourceDocumentKind: "F",
            number: "BAX",
            issueDate: new System.DateTime(2026, 1, 20),
            sourceReference: "encheresv6:ba:1",
            supplier: null,
            totals: new PivotTotalsDto(lines.Count > 0 ? 120m : 0m, 0m, 120m),
            operationCategory: null,
            customer: null,
            lines: [.. lines, .. buyerFees ?? []]);
}

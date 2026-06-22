namespace Liakont.Modules.Pipeline.Tests.Unit.Margin;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Domain.Margin;
using Xunit;

/// <summary>
/// Tests du cœur PUR du calcul de la marge e-reporting B2C (B2C-09b, F03 §2.4) : marge par lot =
/// Σ frais acheteur + Σ frais vendeur, en decimal half-up ; et la garde BLOQUANTE art. 297 E (aucune
/// TVA distincte). AUCUNE règle fiscale inventée — la formule est celle ancrée et validée à
/// GATE_B2C_SOURCING (CGI art. 297 A I-2° + BOI-TVA-SECT-90-50 §270).
/// </summary>
public sealed class MarginCalculatorTests
{
    private static readonly DateTime IssueDate = new(2026, 6, 1);

    [Fact]
    public void Margin_per_lot_is_sum_of_buyer_and_seller_fees_with_two_distinct_lots()
    {
        var document = Declaration(
            sellerFees: new[]
            {
                Seller("no_ba=5000", 80.00m),
                Seller("no_ba=6000", 40.00m),
            },
            buyerFees: new[]
            {
                Buyer("no_ba=5000", 50.00m),
                Buyer("no_ba=6000", 30.00m),
            });

        var result = MarginCalculator.Calculate(document);

        result.Lots.Should().HaveCount(2);

        // Ordre déterministe : 1re apparition dans les frais vendeur.
        result.Lots[0].Should().BeEquivalentTo(new
        {
            LotReference = "no_ba=5000",
            SellerFeesTotal = 80.00m,
            BuyerFeesTotal = 50.00m,
            MarginAmount = 130.00m,
        });
        result.Lots[1].Should().BeEquivalentTo(new
        {
            LotReference = "no_ba=6000",
            SellerFeesTotal = 40.00m,
            BuyerFeesTotal = 30.00m,
            MarginAmount = 70.00m,
        });

        result.TotalMargin.Should().Be(200.00m);
    }

    [Fact]
    public void Multiple_fees_on_same_lot_are_summed()
    {
        var document = Declaration(
            sellerFees: new[]
            {
                Seller("no_ba=5000", 80.00m),
                Seller("no_ba=5000", 20.00m),
            },
            buyerFees: new[]
            {
                Buyer("no_ba=5000", 50.00m),
                Buyer("no_ba=5000", 5.00m),
            });

        var result = MarginCalculator.Calculate(document);

        result.Lots.Should().ContainSingle();
        result.Lots[0].SellerFeesTotal.Should().Be(100.00m);
        result.Lots[0].BuyerFeesTotal.Should().Be(55.00m);
        result.Lots[0].MarginAmount.Should().Be(155.00m);
        result.TotalMargin.Should().Be(155.00m);
    }

    [Fact]
    public void Margin_is_rounded_half_up_to_two_decimals()
    {
        // 0.005 → 0.01 (half-up away-from-zero, CLAUDE.md n°1). Frais bruts non encore au pas de 2 décimales.
        var document = Declaration(
            sellerFees: new[] { Seller("no_ba=5000", 0.005m) },
            buyerFees: new[] { Buyer("no_ba=5000", 0.004m) });

        var result = MarginCalculator.Calculate(document);

        result.Lots[0].SellerFeesTotal.Should().Be(0.01m);
        result.Lots[0].BuyerFeesTotal.Should().Be(0.00m);
        result.Lots[0].MarginAmount.Should().Be(0.01m);
        result.TotalMargin.Should().Be(0.01m);
    }

    [Fact]
    public void Seller_fees_only_are_a_valid_margin_leg()
    {
        var document = Declaration(sellerFees: new[] { Seller("no_ba=5000", 80.00m) });

        var result = MarginCalculator.Calculate(document);

        result.Lots.Should().ContainSingle();
        result.Lots[0].SellerFeesTotal.Should().Be(80.00m);
        result.Lots[0].BuyerFeesTotal.Should().Be(0.00m);
        result.Lots[0].MarginAmount.Should().Be(80.00m);
    }

    [Fact]
    public void Buyer_fees_only_are_a_valid_margin_leg()
    {
        var document = Declaration(buyerFees: new[] { Buyer("no_ba=5000", 50.00m) });

        var result = MarginCalculator.Calculate(document);

        result.Lots.Should().ContainSingle();
        result.Lots[0].BuyerFeesTotal.Should().Be(50.00m);
        result.Lots[0].SellerFeesTotal.Should().Be(0.00m);
        result.Lots[0].MarginAmount.Should().Be(50.00m);
    }

    [Fact]
    public void No_fees_yields_an_empty_result()
    {
        var document = Declaration();

        var result = MarginCalculator.Calculate(document);

        result.Lots.Should().BeEmpty();
        result.TotalMargin.Should().Be(0m);
    }

    [Fact]
    public void Exempt_adjudication_line_with_zero_vat_does_not_violate_297E()
    {
        // Adjudication E / 0 % / VATEX (TaxAmount = 0) — le cas nominal du régime de la marge (F03 §2.3).
        var document = Declaration(
            totalTax: 0m,
            lines: new[] { Line(taxAmount: 0m, rate: 0m, category: VatCategory.E) },
            sellerFees: new[] { Seller("no_ba=5000", 80.00m) },
            buyerFees: new[] { Buyer("no_ba=5000", 50.00m) });

        var act = () => MarginCalculator.Calculate(document);

        act.Should().NotThrow();
    }

    [Fact]
    public void Document_total_tax_greater_than_zero_blocks_297E()
    {
        var document = Declaration(
            totalTax: 20.00m,
            sellerFees: new[] { Seller("no_ba=5000", 80.00m) },
            buyerFees: new[] { Buyer("no_ba=5000", 50.00m) });

        var act = () => MarginCalculator.Calculate(document);

        act.Should().Throw<MarginVatNotSeparableException>()
            .WithMessage("*297 E*");
    }

    [Fact]
    public void Line_with_distinct_vat_blocks_297E()
    {
        // Une ligne portant une TVA distincte (S / 20 % / TaxAmount > 0) viole l'art. 297 E sur la marge.
        var document = Declaration(
            totalTax: 0m,
            lines: new[] { Line(taxAmount: 16.00m, rate: 20m, category: VatCategory.S) },
            sellerFees: new[] { Seller("no_ba=5000", 80.00m) },
            buyerFees: new[] { Buyer("no_ba=5000", 50.00m) });

        var act = () => MarginCalculator.Calculate(document);

        act.Should().Throw<MarginVatNotSeparableException>();
    }

    [Fact]
    public void Null_document_throws_argument_null()
    {
        var act = () => MarginCalculator.Calculate(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static PivotDocumentDto Declaration(
        decimal totalTax = 0m,
        IReadOnlyList<PivotLineDto>? lines = null,
        IReadOnlyList<PivotSellerFeeDto>? sellerFees = null,
        IReadOnlyList<PivotBuyerFeeDto>? buyerFees = null) =>
        new(
            sourceDocumentKind: "10.3",
            number: "B2C-REPORT-001",
            issueDate: IssueDate,
            sourceReference: "src#1",
            supplier: null,
            totals: new PivotTotalsDto(totalNet: 0m, totalTax: totalTax, totalGross: 0m),
            operationCategory: null,
            lines: lines,
            isB2cReportingDeclaration: true,
            sellerFees: sellerFees,
            buyerFees: buyerFees);

    private static PivotSellerFeeDto Seller(string lotReference, decimal netAmount) =>
        new(lotReference: lotReference, netAmount: netAmount, sourceRegimeCode: null, sourceLineRef: null, description: null);

    private static PivotBuyerFeeDto Buyer(string lotReference, decimal netAmount) =>
        new(lotReference: lotReference, netAmount: netAmount, sourceRegimeCode: null, sourceLineRef: null, description: null);

    private static PivotLineDto Line(decimal taxAmount, decimal? rate, VatCategory category) =>
        new(
            description: "ligne",
            netAmount: 100.00m,
            taxes: new[] { new PivotLineTaxDto(taxAmount: taxAmount, rate: rate, categoryCode: category) });
}

namespace Liakont.Modules.Transmission.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Couvre la liste <b>FERMÉE</b> des codes e-reporting (TT-81 / G1.68 et TT-15 / G7.52 ; F03 §2.6) :
/// conversion canonique dans les deux sens et <b>REJET fail-closed</b> de tout code hors liste, y compris
/// la coquille « TLS1 » (CLAUDE.md n°2/3). Le DTO de transport agnostique <see cref="B2cReportingTransaction"/>
/// porte bien des montants <see cref="decimal"/> (n°1).
/// </summary>
public sealed class EReportingCodesTests
{
    [Theory]
    [InlineData(EReportingTransactionCategory.Tlb1, "TLB1")]
    [InlineData(EReportingTransactionCategory.Tps1, "TPS1")]
    [InlineData(EReportingTransactionCategory.Tnt1, "TNT1")]
    [InlineData(EReportingTransactionCategory.Tma1, "TMA1")]
    public void TransactionCategory_RoundTrips_CanonicalCode(EReportingTransactionCategory category, string code)
    {
        category.ToTransactionCategoryCode().Should().Be(code);
        EReportingCodes.TryParseTransactionCategory(code, out var parsed).Should().BeTrue();
        parsed.Should().Be(category);
    }

    [Theory]
    [InlineData("TLS1")] // coquille connue (F03 §2.6) — JAMAIS valide
    [InlineData("tlb1")] // casse
    [InlineData("TMA")]
    [InlineData("")]
    [InlineData(null)]
    public void TransactionCategory_RejectsAnyCodeOutsideClosedList(string? code)
    {
        EReportingCodes.TryParseTransactionCategory(code, out _).Should()
            .BeFalse("la codelist TT-81 est fermée a TLB1/TPS1/TNT1/TMA1 (G1.68) — fail-closed, jamais deviné");
    }

    [Theory]
    [InlineData(EReportingDeclarantRole.Buyer, "BY")]
    [InlineData(EReportingDeclarantRole.Seller, "SE")]
    public void DeclarantRole_RoundTrips_CanonicalCode(EReportingDeclarantRole role, string code)
    {
        role.ToDeclarantRoleCode().Should().Be(code);
        EReportingCodes.TryParseDeclarantRole(code, out var parsed).Should().BeTrue();
        parsed.Should().Be(role);
    }

    [Theory]
    [InlineData("XX")]
    [InlineData("se")]
    [InlineData(null)]
    public void DeclarantRole_RejectsAnyCodeOutsideClosedList(string? code)
    {
        EReportingCodes.TryParseDeclarantRole(code, out _).Should().BeFalse();
    }

    [Fact]
    public void B2cReportingTransaction_ForMargin_CarriesTma1_Seller_AndDecimalAmounts()
    {
        var tx = new B2cReportingTransaction
        {
            Category = EReportingTransactionCategory.Tma1,
            Role = EReportingDeclarantRole.Seller,
            CurrencyCode = "EUR",
            Date = new DateOnly(2026, 6, 5),
            TaxExclusiveAmount = 100.00m,
            TaxTotal = 20.00m,
            Subtotals =
            [
                new B2cReportingTransactionSubtotal { TaxPercent = 20.0m, TaxableAmount = 100.00m, TaxTotal = 20.00m },
            ],
        };

        tx.Category.ToTransactionCategoryCode().Should().Be("TMA1");
        tx.Role.ToDeclarantRoleCode().Should().Be("SE");
        tx.Subtotals.Should().ContainSingle();
    }
}

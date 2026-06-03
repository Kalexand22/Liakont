namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.UI.Helpers;
using Xunit;

/// <summary>
/// Unit tests for <see cref="MoneyParser"/>.
/// Covers free-form monetary input parsing: dot-decimal, comma-decimal, EU/US formats,
/// whitespace stripping, and invalid inputs.
/// </summary>
public sealed class MoneyParserTests
{
    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("0.5", 0.5)]
    [InlineData("-42.00", -42.00)]
    [InlineData("1234", 1234)]
    public void TryParseShouldReturnExpectedValueForDotDecimalInput(string input, decimal expected)
    {
        MoneyParser.TryParse(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1234,56", 1234.56)]
    [InlineData("0,5", 0.5)]
    [InlineData(",99", 0.99)]
    public void TryParseShouldReturnExpectedValueForCommaDecimalInput(string input, decimal expected)
    {
        MoneyParser.TryParse(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1.234.567,89", 1234567.89)]
    [InlineData("10.000,00", 10000.00)]
    public void TryParseShouldReturnExpectedValueForEuFormat(string input, decimal expected)
    {
        MoneyParser.TryParse(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1,234,567.89", 1234567.89)]
    [InlineData("10,000.00", 10000.00)]
    public void TryParseShouldReturnExpectedValueForUsFormat(string input, decimal expected)
    {
        MoneyParser.TryParse(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1,234,567", 1234567)]
    [InlineData("1.234.567", 1234567)]
    public void TryParseShouldReturnExpectedValueWhenAllSeparatorsAreThousands(string input, decimal expected)
    {
        MoneyParser.TryParse(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("  1234.56  ", 1234.56)]
    [InlineData("1\u00a0234,56", 1234.56)]
    public void TryParseShouldStripWhitespaceBeforeParsing(string input, decimal expected)
    {
        MoneyParser.TryParse(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.2.3,4,5")]
    public void TryParseShouldReturnFalseForInvalidInput(string input)
    {
        MoneyParser.TryParse(input, out _).Should().BeFalse();
    }
}

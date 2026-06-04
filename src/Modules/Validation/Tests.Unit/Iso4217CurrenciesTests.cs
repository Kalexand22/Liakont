namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Validation.Domain;
using Xunit;

public sealed class Iso4217CurrenciesTests
{
    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("GBP")]
    [InlineData("XOF")]
    [InlineData("eur")]
    public void Valid_iso_4217_codes_are_accepted(string code)
    {
        Iso4217Currencies.IsValid(code).Should().BeTrue();
    }

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("XYZ")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unknown_or_empty_codes_are_rejected(string? code)
    {
        Iso4217Currencies.IsValid(code).Should().BeFalse();
    }
}

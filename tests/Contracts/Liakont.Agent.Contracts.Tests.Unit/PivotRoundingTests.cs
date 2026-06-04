namespace Liakont.Agent.Contracts.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts;
using Xunit;

/// <summary>
/// Arrondi canonique des montants (acceptance PIV01 « arrondis ») : arrondi commercial half-up
/// (away-from-zero) à 2 décimales, identique pour montants positifs et négatifs (avoirs). La
/// précision exacte de <see cref="decimal"/> est ce qui rend ces cas déterministes — les mêmes
/// valeurs en double divergeraient (CLAUDE.md n°1).
/// </summary>
public sealed class PivotRoundingTests
{
    [Theory]
    [InlineData("1.005", "1.01")] // demi vers le haut (away-from-zero), pas banquier
    [InlineData("1.004", "1.00")] // sous le demi, vers le bas
    [InlineData("2.675", "2.68")] // exact en decimal (faux en double : donnerait 2.67)
    [InlineData("-1.005", "-1.01")] // avoir : symétrique, away-from-zero
    [InlineData("10", "10")] // déjà entier
    [InlineData("8.329999999999998", "8.33")] // flottant sale legacy (ADR-0004 D3-7)
    public void RoundAmount_Should_Round_Half_Up_To_Two_Decimals(string input, string expected)
    {
        var value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        var want = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        PivotRounding.RoundAmount(value).Should().Be(want);
    }

    [Fact]
    public void AmountScale_Should_Be_Two()
    {
        PivotRounding.AmountScale.Should().Be(2);
    }
}

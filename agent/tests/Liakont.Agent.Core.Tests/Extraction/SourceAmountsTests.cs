namespace Liakont.Agent.Core.Tests.Extraction;

using System;
using FluentAssertions;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Conversion gardée float→decimal des montants source (ADR-0004 D3-7, CLAUDE.md n°1) : arrondi
/// commercial half-up au centime, nettoyage du bruit binaire legacy, et erreurs TYPÉES (F01-F02 R7)
/// sur NaN / Infini / hors-plage — jamais d'arrondi à l'aveugle.
/// </summary>
public class SourceAmountsTests
{
    [Theory]
    [InlineData(1.005, 1.01)] // half-up (away-from-zero)
    [InlineData(1.004, 1.00)]
    [InlineData(-1.005, -1.01)] // half-up vaut aussi pour les avoirs (négatifs)
    [InlineData(8.329999999999998, 8.33)] // bruit binaire legacy nettoyé par la cast double->decimal
    [InlineData(100.0, 100.00)]
    public void RoundAmount_rounds_half_up_to_two_decimals(double raw, decimal expected)
    {
        SourceAmounts.RoundAmount(raw, "montant").Should().Be(expected);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RoundAmount_throws_typed_on_non_finite(double raw)
    {
        Action act = () => SourceAmounts.RoundAmount(raw, "montant");

        act.Should().Throw<SourceSchemaException>().Which.Message.Should().Contain("montant");
    }

    [Fact]
    public void RoundAmount_throws_typed_on_overflow()
    {
        Action act = () => SourceAmounts.RoundAmount(double.MaxValue, "montant");

        act.Should().Throw<SourceSchemaException>();
    }

    [Theory]
    [InlineData(5.5, 5.5)] // taux : converti sans arrondi supplémentaire
    [InlineData(20.0, 20.0)]
    [InlineData(0.0, 0.0)]
    public void ToDecimal_converts_without_rounding(double raw, decimal expected)
    {
        SourceAmounts.ToDecimal(raw, "taux").Should().Be(expected);
    }

    [Fact]
    public void ToDecimal_throws_typed_on_non_finite()
    {
        Action act = () => SourceAmounts.ToDecimal(double.NaN, "taux");

        act.Should().Throw<SourceSchemaException>().Which.Message.Should().Contain("taux");
    }
}

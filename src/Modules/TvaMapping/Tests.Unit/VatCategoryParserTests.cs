namespace Liakont.Modules.TvaMapping.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain.Services;
using Xunit;

/// <summary>
/// Parsing des codes catégorie UNCL5305 (F03 §2.1) : toute valeur hors liste est rejetée, jamais
/// devinée (CLAUDE.md n°2). Consommé par l'import de seed (TVA04) et l'édition console (TVA05).
/// </summary>
public sealed class VatCategoryParserTests
{
    [Theory]
    [InlineData("S", VatCategory.S)]
    [InlineData("AA", VatCategory.AA)]
    [InlineData("AAA", VatCategory.AAA)]
    [InlineData("Z", VatCategory.Z)]
    [InlineData("E", VatCategory.E)]
    [InlineData("AE", VatCategory.AE)]
    [InlineData("G", VatCategory.G)]
    [InlineData("K", VatCategory.K)]
    [InlineData("O", VatCategory.O)]
    public void Parse_Admitted_Codes_Succeeds(string code, VatCategory expected)
    {
        VatCategoryParser.Parse(code).Should().Be(expected);
    }

    [Fact]
    public void Parse_Trims_Whitespace()
    {
        VatCategoryParser.Parse("  E  ").Should().Be(VatCategory.E);
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("L")]
    [InlineData("s")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Unknown_Or_Empty_Throws(string code)
    {
        var act = () => VatCategoryParser.Parse(code);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        var act = () => VatCategoryParser.Parse(null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_Numeric_Value_Is_Rejected()
    {
        // « 5 » ne doit JAMAIS être interprété comme la valeur numérique de l'enum (E).
        var act = () => VatCategoryParser.Parse("5");
        act.Should().Throw<ArgumentException>();
    }
}

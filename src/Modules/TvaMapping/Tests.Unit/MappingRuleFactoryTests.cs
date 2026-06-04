namespace Liakont.Modules.TvaMapping.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Domain.Services;
using Xunit;

/// <summary>
/// Construction d'une règle de mapping à partir d'entrées primitives (item TVA05) : les énumérations
/// sont parsées strictement (jamais une valeur hors liste sourcée), le code régime est obligatoire.
/// </summary>
public sealed class MappingRuleFactoryTests
{
    [Fact]
    public void Create_Builds_Rule_From_Strings()
    {
        var rule = MappingRuleFactory.Create(
            "REGIME-A", "Assujetti 20 %", "Adjudication", null, "S", null, null, "Fixed", 20m);

        rule.SourceRegimeCode.Should().Be("REGIME-A");
        rule.Part.Should().Be(MappingPart.Adjudication);
        rule.Category.Should().Be(VatCategory.S);
        rule.RateMode.Should().Be(RateMode.Fixed);
        rule.RateValue.Should().Be(20m);
    }

    [Fact]
    public void Create_Trims_Optional_Fields_To_Null()
    {
        var rule = MappingRuleFactory.Create(
            "REGIME-A", "  ", "Frais", null, "S", "  ", "  ", "ComputedFromSource", null);

        rule.Label.Should().BeNull();
        rule.Vatex.Should().BeNull();
        rule.Note.Should().BeNull();
        rule.RateMode.Should().Be(RateMode.ComputedFromSource);
    }

    [Fact]
    public void Create_Empty_Code_Throws()
    {
        var act = () => MappingRuleFactory.Create(
            "  ", null, "Adjudication", null, "S", null, null, "Fixed", 20m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Unknown_Part_Throws()
    {
        var act = () => MappingRuleFactory.Create(
            "REGIME-A", null, "Inconnue", null, "S", null, null, "Fixed", 20m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Unknown_Category_Throws()
    {
        var act = () => MappingRuleFactory.Create(
            "REGIME-A", null, "Adjudication", null, "XYZ", null, null, "Fixed", 20m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Unknown_RateMode_Throws()
    {
        var act = () => MappingRuleFactory.Create(
            "REGIME-A", null, "Adjudication", null, "S", null, null, "Approximatif", 20m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Preserves_SourceFlags()
    {
        var rule = MappingRuleFactory.Create(
            "REGIME-6", null, "Adjudication",
            new Dictionary<string, string> { ["RegimeMarge"] = "true" },
            "E", "VATEX-EU-J", null, "Fixed", 0m);

        rule.SourceFlags.Should().NotBeNull();
        rule.SourceFlags!["RegimeMarge"].Should().Be("true");
    }
}

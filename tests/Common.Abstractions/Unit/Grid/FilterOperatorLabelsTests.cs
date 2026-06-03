namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class FilterOperatorLabelsTests
{
    [Theory]
    [InlineData(FilterOperator.Equals, "Égal à")]
    [InlineData(FilterOperator.NotContains, "Ne contient pas")]
    [InlineData(FilterOperator.EndsWith, "Se termine par")]
    [InlineData(FilterOperator.NotIn, "Pas parmi")]
    [InlineData(FilterOperator.GreaterThanOrEqual, "Supérieur ou égal à")]
    [InlineData(FilterOperator.LessThanOrEqual, "Inférieur ou égal à")]
    [InlineData(FilterOperator.NotBetween, "Pas entre")]
    [InlineData(FilterOperator.Before, "Avant")]
    [InlineData(FilterOperator.After, "Après")]
    [InlineData(FilterOperator.RelativePeriod, "Période relative")]
    public void GetLabelShouldReturnFrenchLabel(FilterOperator op, string expected)
    {
        FilterOperatorLabels.GetLabel(op).Should().Be(expected);
    }

    [Fact]
    public void AllOperatorsShouldHaveLabels()
    {
        foreach (var op in Enum.GetValues<FilterOperator>())
        {
            var label = FilterOperatorLabels.GetLabel(op);
            label.Should().NotBe(
                op.ToString(),
                $"operator {op} should have a French label, not its enum name");
        }
    }
}

namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class FilterOperatorMapTests
{
    [Theory]
    [InlineData(ColumnDataType.Text, FilterOperator.Contains, true)]
    [InlineData(ColumnDataType.Text, FilterOperator.StartsWith, true)]
    [InlineData(ColumnDataType.Text, FilterOperator.Equals, true)]
    [InlineData(ColumnDataType.Text, FilterOperator.Between, false)]
    [InlineData(ColumnDataType.Text, FilterOperator.GreaterThan, false)]
    [InlineData(ColumnDataType.Number, FilterOperator.GreaterThan, true)]
    [InlineData(ColumnDataType.Number, FilterOperator.Between, true)]
    [InlineData(ColumnDataType.Number, FilterOperator.Contains, false)]
    [InlineData(ColumnDataType.Date, FilterOperator.Between, true)]
    [InlineData(ColumnDataType.Date, FilterOperator.GreaterThan, true)]
    [InlineData(ColumnDataType.Date, FilterOperator.Contains, false)]
    [InlineData(ColumnDataType.Boolean, FilterOperator.Equals, true)]
    [InlineData(ColumnDataType.Boolean, FilterOperator.Contains, false)]
    [InlineData(ColumnDataType.Boolean, FilterOperator.Between, false)]
    [InlineData(ColumnDataType.Money, FilterOperator.GreaterThan, true)]
    [InlineData(ColumnDataType.Money, FilterOperator.Between, true)]
    [InlineData(ColumnDataType.Enum, FilterOperator.In, true)]
    [InlineData(ColumnDataType.Enum, FilterOperator.Equals, true)]
    [InlineData(ColumnDataType.Enum, FilterOperator.Contains, false)]
    [InlineData(ColumnDataType.Text, FilterOperator.NotContains, true)]
    [InlineData(ColumnDataType.Text, FilterOperator.EndsWith, true)]
    [InlineData(ColumnDataType.Text, FilterOperator.NotIn, true)]
    [InlineData(ColumnDataType.Number, FilterOperator.GreaterThanOrEqual, true)]
    [InlineData(ColumnDataType.Number, FilterOperator.LessThanOrEqual, true)]
    [InlineData(ColumnDataType.Number, FilterOperator.NotBetween, true)]
    [InlineData(ColumnDataType.Number, FilterOperator.NotIn, true)]
    [InlineData(ColumnDataType.Date, FilterOperator.Before, true)]
    [InlineData(ColumnDataType.Date, FilterOperator.After, true)]
    [InlineData(ColumnDataType.Date, FilterOperator.RelativePeriod, true)]
    [InlineData(ColumnDataType.Date, FilterOperator.GreaterThanOrEqual, true)]
    [InlineData(ColumnDataType.Date, FilterOperator.NotBetween, true)]
    [InlineData(ColumnDataType.Money, FilterOperator.GreaterThanOrEqual, true)]
    [InlineData(ColumnDataType.Money, FilterOperator.NotBetween, true)]
    [InlineData(ColumnDataType.Enum, FilterOperator.NotIn, true)]
    [InlineData(ColumnDataType.Boolean, FilterOperator.IsNull, true)]
    [InlineData(ColumnDataType.Boolean, FilterOperator.IsNotNull, true)]
    public void IsOperatorValidShouldReturnExpectedResult(
        ColumnDataType dataType, FilterOperator op, bool expected)
    {
        FilterOperatorMap.IsOperatorValid(dataType, op).Should().Be(expected);
    }

    [Fact]
    public void GetOperatorsShouldReturnNonEmptyListForAllTypes()
    {
        foreach (var dataType in System.Enum.GetValues<ColumnDataType>())
        {
            FilterOperatorMap.GetOperators(dataType).Should().NotBeEmpty(
                $"every ColumnDataType should have at least one valid operator, but {dataType} has none");
        }
    }

    [Fact]
    public void TextShouldSupportContainsAndStartsWith()
    {
        var operators = FilterOperatorMap.GetOperators(ColumnDataType.Text);

        operators.Should().Contain(FilterOperator.Contains);
        operators.Should().Contain(FilterOperator.StartsWith);
    }

    [Fact]
    public void BooleanShouldSupportEqualsNotEqualsAndNullChecks()
    {
        var operators = FilterOperatorMap.GetOperators(ColumnDataType.Boolean);

        operators.Should().HaveCount(4);
        operators.Should().Contain(FilterOperator.Equals);
        operators.Should().Contain(FilterOperator.NotEquals);
        operators.Should().Contain(FilterOperator.IsNull);
        operators.Should().Contain(FilterOperator.IsNotNull);
    }
}

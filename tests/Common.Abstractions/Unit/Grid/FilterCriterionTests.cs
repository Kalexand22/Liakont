namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class FilterCriterionTests
{
    [Fact]
    public void RecordShouldPreserveAllProperties()
    {
        var criterion = new FilterCriterion("Customer.City", FilterOperator.Equals, "Paris");

        criterion.Field.Should().Be("Customer.City");
        criterion.Operator.Should().Be(FilterOperator.Equals);
        criterion.Value.Should().Be("Paris");
        criterion.ValueEnd.Should().BeNull();
    }

    [Fact]
    public void BetweenShouldPreserveValueEnd()
    {
        var criterion = new FilterCriterion("Amount", FilterOperator.Between, 100m, 500m);

        criterion.Value.Should().Be(100m);
        criterion.ValueEnd.Should().Be(500m);
    }

    [Fact]
    public void IsNullShouldHaveNullValue()
    {
        var criterion = new FilterCriterion("Notes", FilterOperator.IsNull, null);

        criterion.Value.Should().BeNull();
    }

    [Fact]
    public void RecordShouldSupportValueEquality()
    {
        var a = new FilterCriterion("Name", FilterOperator.Contains, "test");
        var b = new FilterCriterion("Name", FilterOperator.Contains, "test");

        a.Should().Be(b);
    }

    [Fact]
    public void RecordShouldSupportWithExpression()
    {
        var original = new FilterCriterion("Name", FilterOperator.Equals, "foo");
        var modified = original with { Operator = FilterOperator.Contains };

        modified.Operator.Should().Be(FilterOperator.Contains);
        modified.Field.Should().Be("Name");
    }
}

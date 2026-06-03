namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class FilterGroupTests
{
    [Fact]
    public void ShouldPreserveLogicAndCriteria()
    {
        var criteria = new List<FilterCriterion>
        {
            new("Name", FilterOperator.Contains, "test"),
            new("Amount", FilterOperator.GreaterThan, 100),
        };

        var group = new FilterGroup(FilterLogic.And, criteria);

        group.Logic.Should().Be(FilterLogic.And);
        group.Criteria.Should().HaveCount(2);
        group.SubGroups.Should().BeNull();
    }

    [Fact]
    public void ShouldSupportNestedSubGroups()
    {
        var inner1 = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Contains, "A"),
            new("Status", FilterOperator.Equals, "Active"),
        });
        var inner2 = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("City", FilterOperator.Equals, "Paris"),
            new("Amount", FilterOperator.GreaterThan, 1000),
        });

        var outer = new FilterGroup(FilterLogic.Or, new List<FilterCriterion>(), new List<FilterGroup> { inner1, inner2 });

        outer.Logic.Should().Be(FilterLogic.Or);
        outer.Criteria.Should().BeEmpty();
        outer.SubGroups.Should().HaveCount(2);
    }

    [Fact]
    public void ShouldSupportMixedCriteriaAndSubGroups()
    {
        var subGroup = new FilterGroup(FilterLogic.Or, new List<FilterCriterion>
        {
            new("Status", FilterOperator.Equals, "Draft"),
            new("Status", FilterOperator.Equals, "Sent"),
        });

        var group = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion> { new("Amount", FilterOperator.GreaterThan, 0) },
            new List<FilterGroup> { subGroup });

        group.Criteria.Should().HaveCount(1);
        group.SubGroups.Should().HaveCount(1);
    }
}

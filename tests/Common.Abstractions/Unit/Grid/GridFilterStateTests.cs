namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class GridFilterStateTests
{
    [Fact]
    public void NewState_HasNoActiveFilters()
    {
        var state = new GridFilterState();

        state.HasActiveFilters.Should().BeFalse();
        state.CriteriaCount.Should().Be(0);
        state.GlobalSearch.Should().BeNull();
        state.SimpleFilters.Should().BeEmpty();
        state.AdvancedFilter.Should().BeNull();
    }

    [Fact]
    public void GlobalSearch_MakesActiveFilters()
    {
        var state = new GridFilterState { GlobalSearch = "test" };

        state.HasActiveFilters.Should().BeTrue();
    }

    [Fact]
    public void GlobalSearch_Whitespace_IsNotActive()
    {
        var state = new GridFilterState { GlobalSearch = "   " };

        state.HasActiveFilters.Should().BeFalse();
    }

    [Fact]
    public void SimpleFilter_MakesActiveFilters()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Name", FilterOperator.Equals, "test"));

        state.HasActiveFilters.Should().BeTrue();
    }

    [Fact]
    public void AdvancedFilter_MakesActiveFilters()
    {
        var group = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("Name", FilterOperator.Equals, "test")]);
        var state = new GridFilterState { AdvancedFilter = group };

        state.HasActiveFilters.Should().BeTrue();
    }

    [Fact]
    public void CriteriaCount_ExcludesGlobalSearch_DF03()
    {
        var state = new GridFilterState { GlobalSearch = "search term" };
        state.AddSimpleFilter(new FilterCriterion("Name", FilterOperator.Equals, "test"));

        state.CriteriaCount.Should().Be(1, "global search is excluded per DF-03");
    }

    [Fact]
    public void CriteriaCount_CombinesSimpleAndAdvanced()
    {
        // GFI16: in the unified model, AddSimpleFilter appends to the root
        // AND of AdvancedFilter, so three distinct criteria should count as
        // three even though they came from two different entry points.
        var group = new FilterGroup(
            FilterLogic.And,
            [
                new FilterCriterion("A", FilterOperator.Equals, 1),
                new FilterCriterion("B", FilterOperator.GreaterThan, 2),
            ]);
        var state = new GridFilterState { AdvancedFilter = group };
        state.AddSimpleFilter(new FilterCriterion("C", FilterOperator.Contains, "x"));

        state.CriteriaCount.Should().Be(3);
        state.AdvancedFilter!.Criteria.Should().HaveCount(3);
    }

    [Fact]
    public void CriteriaCount_CountsNestedSubGroups()
    {
        var subGroup = new FilterGroup(
            FilterLogic.And,
            [
                new FilterCriterion("B", FilterOperator.Equals, 2),
                new FilterCriterion("C", FilterOperator.Equals, 3),
            ]);
        var group = new FilterGroup(
            FilterLogic.Or,
            [new FilterCriterion("A", FilterOperator.Equals, 1)],
            [subGroup]);
        var state = new GridFilterState { AdvancedFilter = group };

        state.CriteriaCount.Should().Be(3);
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        var group = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("A", FilterOperator.Equals, 1)]);
        var state = new GridFilterState
        {
            GlobalSearch = "test",
            AdvancedFilter = group,
        };
        state.AddSimpleFilter(new FilterCriterion("B", FilterOperator.Contains, "x"));

        state.ClearAll();

        state.GlobalSearch.Should().BeNull();
        state.SimpleFilters.Should().BeEmpty();
        state.AdvancedFilter.Should().BeNull();
        state.HasActiveFilters.Should().BeFalse();
        state.CriteriaCount.Should().Be(0);
    }

    [Fact]
    public void AddSimpleFilter_AppendsInOrder()
    {
        var state = new GridFilterState();
        var c1 = new FilterCriterion("A", FilterOperator.Equals, "1");
        var c2 = new FilterCriterion("B", FilterOperator.Equals, "2");

        state.AddSimpleFilter(c1);
        state.AddSimpleFilter(c2);

        state.SimpleFilters.Should().ContainInOrder(c1, c2);
    }

    [Fact]
    public void AddSimpleFilter_NullCriterion_Throws()
    {
        var state = new GridFilterState();

        var act = () => state.AddSimpleFilter(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveSimpleFilter_RemovesExistingCriterion()
    {
        var state = new GridFilterState();
        var criterion = new FilterCriterion("A", FilterOperator.Equals, "1");
        state.AddSimpleFilter(criterion);

        state.RemoveSimpleFilter(criterion);

        state.SimpleFilters.Should().BeEmpty();
    }

    [Fact]
    public void RemoveSimpleFilter_NonExistentCriterion_IsNoOp()
    {
        var state = new GridFilterState();
        var criterion = new FilterCriterion("A", FilterOperator.Equals, "1");

        var act = () => state.RemoveSimpleFilter(criterion);

        act.Should().NotThrow();
        state.SimpleFilters.Should().BeEmpty();
    }

    [Fact]
    public void ClearSimpleFilters_RemovesAllCriteria()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("A", FilterOperator.Equals, "1"));
        state.AddSimpleFilter(new FilterCriterion("B", FilterOperator.Equals, "2"));

        state.ClearSimpleFilters();

        state.SimpleFilters.Should().BeEmpty();
        state.HasActiveFilters.Should().BeFalse();
    }

    [Fact]
    public void SimpleFilters_IsReadOnly()
    {
        var state = new GridFilterState();

        state.SimpleFilters.Should().BeAssignableTo<IReadOnlyList<FilterCriterion>>();
    }

    [Fact]
    public void AddSimpleFilter_WritesIntoAdvancedFilterRoot_GFI16()
    {
        // The unified filter tree means simple filters ARE the flat root criteria
        // of AdvancedFilter — so a bare AddSimpleFilter call must materialize
        // AdvancedFilter, not a separate list.
        var state = new GridFilterState();

        state.AddSimpleFilter(new FilterCriterion("Name", FilterOperator.Equals, "Acme"));

        state.AdvancedFilter.Should().NotBeNull();
        state.AdvancedFilter!.Logic.Should().Be(FilterLogic.And);
        state.AdvancedFilter.Criteria.Should().HaveCount(1);
        state.AdvancedFilter.Criteria[0].Field.Should().Be("Name");
        state.SimpleFilters.Should().HaveCount(1);
    }

    [Fact]
    public void AddSimpleFilter_AppendsToExistingAndRoot_GFI16()
    {
        var state = new GridFilterState
        {
            AdvancedFilter = new FilterGroup(
                FilterLogic.And,
                [new FilterCriterion("Country", FilterOperator.Equals, "FR")]),
        };

        state.AddSimpleFilter(new FilterCriterion("Status", FilterOperator.Equals, "Active"));

        state.AdvancedFilter!.Criteria.Should().HaveCount(2);
        state.SimpleFilters.Should().HaveCount(2);
    }

    [Fact]
    public void AddSimpleFilter_WrapsOrRootAsSubGroup_GFI16()
    {
        // An OR root cannot accept a simple AND append without changing semantics,
        // so the existing expression is preserved inside a new AND root.
        var orRoot = new FilterGroup(
            FilterLogic.Or,
            [
                new FilterCriterion("Status", FilterOperator.Equals, "Draft"),
                new FilterCriterion("Status", FilterOperator.Equals, "Review"),
            ]);
        var state = new GridFilterState { AdvancedFilter = orRoot };

        state.AddSimpleFilter(new FilterCriterion("Amount", FilterOperator.GreaterThan, 100));

        state.AdvancedFilter!.Logic.Should().Be(FilterLogic.And);
        state.AdvancedFilter.Criteria.Should().HaveCount(1);
        state.AdvancedFilter.Criteria[0].Field.Should().Be("Amount");
        state.AdvancedFilter.SubGroups.Should().NotBeNull();
        state.AdvancedFilter.SubGroups!.Should().ContainSingle().Which.Should().Be(orRoot);
    }

    [Fact]
    public void AddSimpleFilter_AppendsToAndRoot_PreservesSubGroups_GFI16()
    {
        var subGroup = new FilterGroup(
            FilterLogic.Or,
            [new FilterCriterion("Status", FilterOperator.Equals, "Draft")]);
        var root = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("Country", FilterOperator.Equals, "FR")],
            [subGroup]);
        var state = new GridFilterState { AdvancedFilter = root };

        state.AddSimpleFilter(new FilterCriterion("Amount", FilterOperator.GreaterThan, 100));

        state.AdvancedFilter!.Logic.Should().Be(FilterLogic.And);
        state.AdvancedFilter.Criteria.Should().HaveCount(2);
        state.AdvancedFilter.SubGroups.Should().ContainSingle().Which.Should().Be(subGroup);
    }

    [Fact]
    public void SimpleFilters_IncludesRootAndCriteria_EvenWithSubGroups_GFI16()
    {
        // GFI16: when the root logic is AND, root criteria remain exposed as
        // "simple filters" even alongside sub-groups, so URL sync can carry
        // them and restore them as shareable chips.
        var subGroup = new FilterGroup(
            FilterLogic.Or,
            [new FilterCriterion("Status", FilterOperator.Equals, "Draft")]);
        var root = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("Country", FilterOperator.Equals, "FR")],
            [subGroup]);
        var state = new GridFilterState { AdvancedFilter = root };

        state.SimpleFilters.Should().ContainSingle()
            .Which.Field.Should().Be("Country");
        state.CriteriaCount.Should().Be(2);
    }

    [Fact]
    public void SimpleFilters_EmptyForFlatOrRoot_GFI16()
    {
        // A flat OR root is shareable only as a whole (decomposing into
        // independent chips would silently flip OR→AND on round-trip), so
        // SimpleFilters is empty even though SubGroups is also empty.
        var root = new FilterGroup(
            FilterLogic.Or,
            [
                new FilterCriterion("Status", FilterOperator.Equals, "Draft"),
                new FilterCriterion("Status", FilterOperator.Equals, "Review"),
            ]);
        var state = new GridFilterState { AdvancedFilter = root };

        state.SimpleFilters.Should().BeEmpty();
        state.CriteriaCount.Should().Be(2);
    }

    [Fact]
    public void RemoveSimpleFilterAt_ClearsAdvancedFilter_WhenLastRootCriterion_GFI16()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Name", FilterOperator.Equals, "Acme"));

        state.RemoveSimpleFilterAt(0);

        state.AdvancedFilter.Should().BeNull();
        state.SimpleFilters.Should().BeEmpty();
    }

    [Fact]
    public void RemoveSimpleFilterAt_PreservesSubGroups_GFI16()
    {
        // GFI16: removing a root criterion keeps the sub-group tree intact.
        var subGroup = new FilterGroup(
            FilterLogic.Or,
            [new FilterCriterion("Status", FilterOperator.Equals, "Draft")]);
        var root = new FilterGroup(
            FilterLogic.And,
            [
                new FilterCriterion("Country", FilterOperator.Equals, "FR"),
                new FilterCriterion("Amount", FilterOperator.GreaterThan, 100),
            ],
            [subGroup]);
        var state = new GridFilterState { AdvancedFilter = root };

        state.RemoveSimpleFilterAt(0);

        state.AdvancedFilter.Should().NotBeNull();
        state.AdvancedFilter!.Criteria.Should().ContainSingle()
            .Which.Field.Should().Be("Amount");
        state.AdvancedFilter.SubGroups.Should().ContainSingle().Which.Should().Be(subGroup);
    }

    [Fact]
    public void RemoveSimpleFilterAt_NoOpOnOrRoot_GFI16()
    {
        var root = new FilterGroup(
            FilterLogic.Or,
            [
                new FilterCriterion("Status", FilterOperator.Equals, "Draft"),
                new FilterCriterion("Status", FilterOperator.Equals, "Review"),
            ]);
        var state = new GridFilterState { AdvancedFilter = root };

        state.RemoveSimpleFilterAt(0);

        state.AdvancedFilter.Should().Be(root);
    }

    [Fact]
    public void ReplaceSimpleFilterAt_PreservesOrderAndRootLogic_GFI16()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("A", FilterOperator.Equals, "1"));
        state.AddSimpleFilter(new FilterCriterion("B", FilterOperator.Equals, "2"));
        state.AddSimpleFilter(new FilterCriterion("C", FilterOperator.Equals, "3"));

        state.ReplaceSimpleFilterAt(1, new FilterCriterion("B", FilterOperator.Equals, "22"));

        state.SimpleFilters.Should().HaveCount(3);
        state.SimpleFilters[0].Value.Should().Be("1");
        state.SimpleFilters[1].Value.Should().Be("22");
        state.SimpleFilters[2].Value.Should().Be("3");
    }

    [Fact]
    public void ClearSimpleFilters_PreservesSubGroups_GFI16()
    {
        var subGroup = new FilterGroup(
            FilterLogic.Or,
            [new FilterCriterion("Status", FilterOperator.Equals, "Draft")]);
        var root = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("Country", FilterOperator.Equals, "FR")],
            [subGroup]);
        var state = new GridFilterState { AdvancedFilter = root };

        state.ClearSimpleFilters();

        state.AdvancedFilter.Should().NotBeNull();
        state.AdvancedFilter!.Criteria.Should().BeEmpty();
        state.AdvancedFilter.SubGroups.Should().ContainSingle().Which.Should().Be(subGroup);
    }
}

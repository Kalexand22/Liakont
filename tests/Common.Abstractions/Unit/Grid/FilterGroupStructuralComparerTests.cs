namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

/// <summary>
/// Covers the GUX07 dirty-detection invariant: record equality on FilterGroup
/// is reference-based on Criteria/SubGroups, so a dedicated structural comparer
/// is required. Each test here guards one behavior that the builder relies on.
/// </summary>
public sealed class FilterGroupStructuralComparerTests
{
    [Fact]
    public void NullAndNullAreEqual()
    {
        FilterGroupStructuralComparer.Equals(null, null).Should().BeTrue();
    }

    [Fact]
    public void NullAndNonNullAreNotEqual()
    {
        var g = new FilterGroup(FilterLogic.And, new List<FilterCriterion>());
        FilterGroupStructuralComparer.Equals(null, g).Should().BeFalse();
        FilterGroupStructuralComparer.Equals(g, null).Should().BeFalse();
    }

    [Fact]
    public void SameReferenceIsEqual()
    {
        var g = new FilterGroup(FilterLogic.Or, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "Alice"),
        });
        FilterGroupStructuralComparer.Equals(g, g).Should().BeTrue();
    }

    [Fact]
    public void StructurallyEqualGroupsFromDifferentInstancesAreEqual()
    {
        var a = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Contains, "acme"),
            new("Amount", FilterOperator.GreaterThan, 100m),
        });
        var b = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Contains, "acme"),
            new("Amount", FilterOperator.GreaterThan, 100m),
        });

        FilterGroupStructuralComparer.Equals(a, b).Should().BeTrue();
    }

    [Fact]
    public void DifferentLogicIsNotEqual()
    {
        var a = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "x"),
        });
        var b = new FilterGroup(FilterLogic.Or, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "x"),
        });

        FilterGroupStructuralComparer.Equals(a, b).Should().BeFalse();
    }

    [Fact]
    public void DifferentCriterionCountIsNotEqual()
    {
        var a = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "x"),
        });
        var b = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "x"),
            new("Amount", FilterOperator.Equals, 1m),
        });

        FilterGroupStructuralComparer.Equals(a, b).Should().BeFalse();
    }

    [Fact]
    public void DifferentCriterionValueIsNotEqual()
    {
        var a = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "alpha"),
        });
        var b = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "beta"),
        });

        FilterGroupStructuralComparer.Equals(a, b).Should().BeFalse();
    }

    [Fact]
    public void DifferentCriterionOrderIsNotEqual()
    {
        var a = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "x"),
            new("Amount", FilterOperator.Equals, 1m),
        });
        var b = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Amount", FilterOperator.Equals, 1m),
            new("Name", FilterOperator.Equals, "x"),
        });

        FilterGroupStructuralComparer.Equals(a, b).Should().BeFalse();
    }

    [Fact]
    public void DifferentValueEndIsNotEqual()
    {
        var a = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Amount", FilterOperator.Between, 10m, 20m),
        });
        var b = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Amount", FilterOperator.Between, 10m, 30m),
        });

        FilterGroupStructuralComparer.Equals(a, b).Should().BeFalse();
    }

    [Fact]
    public void NullSubGroupsAndEmptySubGroupsAreEqual()
    {
        var a = new FilterGroup(FilterLogic.And, new List<FilterCriterion>());
        var b = new FilterGroup(FilterLogic.And, new List<FilterCriterion>(), SubGroups: new List<FilterGroup>());

        FilterGroupStructuralComparer.Equals(a, b).Should().BeTrue();
    }

    [Fact]
    public void NestedEqualSubGroupsAreEqual()
    {
        var inner = new FilterGroup(FilterLogic.Or, new List<FilterCriterion>
        {
            new("City", FilterOperator.Equals, "Paris"),
        });
        var a = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion> { new("Name", FilterOperator.Contains, "test") },
            new List<FilterGroup> { inner });
        var b = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion> { new("Name", FilterOperator.Contains, "test") },
            new List<FilterGroup>
            {
                new(FilterLogic.Or, new List<FilterCriterion>
                {
                    new("City", FilterOperator.Equals, "Paris"),
                }),
            });

        FilterGroupStructuralComparer.Equals(a, b).Should().BeTrue();
    }

    [Fact]
    public void NestedSubGroupDifferenceIsNotEqual()
    {
        var a = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion>(),
            new List<FilterGroup>
            {
                new(FilterLogic.Or, new List<FilterCriterion>
                {
                    new("City", FilterOperator.Equals, "Paris"),
                }),
            });
        var b = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion>(),
            new List<FilterGroup>
            {
                new(FilterLogic.Or, new List<FilterCriterion>
                {
                    new("City", FilterOperator.Equals, "Lyon"),
                }),
            });

        FilterGroupStructuralComparer.Equals(a, b).Should().BeFalse();
    }

    [Fact]
    public void NumericValueEqualityAcrossTypesIsRecognized()
    {
        // Loaded filter deserialized "100" as decimal; builder stores "100" as
        // string. Invariant-string normalization lets the comparer see them
        // as equal so the loaded filter stays "clean" in the builder.
        var loaded = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Amount", FilterOperator.Equals, 100m),
        });
        var builderState = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Amount", FilterOperator.Equals, "100"),
        });

        FilterGroupStructuralComparer.Equals(loaded, builderState).Should().BeTrue();
    }

    [Fact]
    public void DecimalValueComparisonIsCultureInvariant()
    {
        // Emulate a fr-FR thread: ToString on decimal in fr-FR yields "100,5",
        // while en-US yields "100.5". Invariant comparison must not flip.
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

            var loaded = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Amount", FilterOperator.Equals, 100.5m),
            });
            var builderState = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Amount", FilterOperator.Equals, "100.5"),
            });

            FilterGroupStructuralComparer.Equals(loaded, builderState).Should().BeTrue();
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void JsonRoundTripFilterGroupRemainsStructurallyEqual()
    {
        // Production path: SavedFilter persisted as JSONB, round-tripped to
        // FilterGroup. JsonElement-backed numbers must compare equal to the
        // original CLR values so an unmodified loaded filter reads as clean.
        var original = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Amount", FilterOperator.Equals, 1500m),
            new("Active", FilterOperator.Equals, true),
            new("Name", FilterOperator.Contains, "acme"),
        });

        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<FilterGroup>(json);

        deserialized.Should().NotBeNull();
        FilterGroupStructuralComparer.Equals(original, deserialized).Should().BeTrue();
    }
}

namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

/// <summary>
/// GFI14 — Round-trip tests for <see cref="GridFilterStateSerializer"/>. The serializer must
/// survive persistence in <c>grid.user_preferences.filter_state</c> (jsonb) and deserialize
/// back to CLR types that the expression builder can coerce via Convert.ChangeType.
/// </summary>
public sealed class GridFilterStateSerializerTests
{
    private static readonly string[] LegacyMergedFieldsInOrder = { "Country", "Type" };

    [Fact]
    public void SerializeShouldReturnNullWhenStateIsEmpty()
    {
        var state = new GridFilterState();
        GridFilterStateSerializer.Serialize(state).Should().BeNull();
    }

    [Fact]
    public void DeserializeShouldReturnEmptyStateForNullOrBlank()
    {
        GridFilterStateSerializer.Deserialize(null).HasActiveFilters.Should().BeFalse();
        GridFilterStateSerializer.Deserialize(string.Empty).HasActiveFilters.Should().BeFalse();
        GridFilterStateSerializer.Deserialize("   ").HasActiveFilters.Should().BeFalse();
    }

    [Fact]
    public void DeserializeShouldReturnEmptyStateOnCorruptJson()
    {
        var result = GridFilterStateSerializer.Deserialize("{not valid json");
        result.HasActiveFilters.Should().BeFalse();
    }

    [Fact]
    public void RoundTripGlobalSearchOnly()
    {
        var state = new GridFilterState { GlobalSearch = "hello world" };

        var json = GridFilterStateSerializer.Serialize(state);
        json.Should().NotBeNull();

        var restored = GridFilterStateSerializer.Deserialize(json);
        restored.GlobalSearch.Should().Be("hello world");
        restored.SimpleFilters.Should().BeEmpty();
        restored.AdvancedFilter.Should().BeNull();
    }

    [Fact]
    public void RoundTripStringCriterion()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Name", FilterOperator.Contains, "Acme"));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        restored.SimpleFilters.Should().HaveCount(1);
        restored.SimpleFilters[0].Field.Should().Be("Name");
        restored.SimpleFilters[0].Operator.Should().Be(FilterOperator.Contains);
        restored.SimpleFilters[0].Value.Should().Be("Acme");
    }

    [Fact]
    public void RoundTripNumberCriterion()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Amount", FilterOperator.GreaterThan, 5000));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        restored.SimpleFilters[0].Value.Should().BeOfType<double>();
        ((double)restored.SimpleFilters[0].Value!).Should().Be(5000d);
    }

    [Fact]
    public void RoundTripDecimalCriterion()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Price", FilterOperator.LessThan, 12.5m));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        // GFI14 fix: decimal must survive as decimal, not be downcast to double.
        restored.SimpleFilters[0].Value.Should().BeOfType<decimal>();
        ((decimal)restored.SimpleFilters[0].Value!).Should().Be(12.5m);
    }

    [Fact]
    public void RoundTripDecimalCriterionPreservesCurrencyPrecision()
    {
        // The naive Convert.ToDouble path loses precision at the 1e-2 place for
        // large currency values (e.g. 1234567.89m → 1234567.8900000001d). The
        // dedicated "dec" tag keeps it exact — prove it with a hostile value.
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Balance", FilterOperator.Equals, 1234567.89m));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        restored.SimpleFilters[0].Value.Should().BeOfType<decimal>();
        ((decimal)restored.SimpleFilters[0].Value!).Should().Be(1234567.89m);
    }

    [Fact]
    public void RoundTripHighPrecisionDecimalCriterion()
    {
        // 28-digit decimal — the widest System.Decimal can represent.
        var state = new GridFilterState();
        var value = 9999999999999999.1234567890m;
        state.AddSimpleFilter(new FilterCriterion("Rate", FilterOperator.Equals, value));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        ((decimal)restored.SimpleFilters[0].Value!).Should().Be(value);
    }

    [Fact]
    public void RoundTripBoolCriterion()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("IsActive", FilterOperator.Equals, true));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        restored.SimpleFilters[0].Value.Should().Be(true);
    }

    [Fact]
    public void RoundTripDateOnlyCriterion()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("StartDate", FilterOperator.Equals, new DateOnly(2026, 3, 15)));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        restored.SimpleFilters[0].Value.Should().Be(new DateOnly(2026, 3, 15));
    }

    [Fact]
    public void RoundTripDateTimeOffsetCriterion()
    {
        var state = new GridFilterState();
        var now = new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.FromHours(2));
        state.AddSimpleFilter(new FilterCriterion("CreatedAt", FilterOperator.After, now));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        var restoredValue = restored.SimpleFilters[0].Value;
        restoredValue.Should().BeOfType<DateTimeOffset>();
        ((DateTimeOffset)restoredValue!).UtcDateTime.Should().Be(now.UtcDateTime);
    }

    [Fact]
    public void RoundTripGuidCriterion()
    {
        var state = new GridFilterState();
        var id = Guid.NewGuid();
        state.AddSimpleFilter(new FilterCriterion("Id", FilterOperator.Equals, id));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        restored.SimpleFilters[0].Value.Should().Be(id);
    }

    [Fact]
    public void RoundTripIsNullCriterion()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Notes", FilterOperator.IsNull, Value: null));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        restored.SimpleFilters[0].Operator.Should().Be(FilterOperator.IsNull);
        restored.SimpleFilters[0].Value.Should().BeNull();
    }

    [Fact]
    public void RoundTripBetweenCriterionWithBothBounds()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Amount", FilterOperator.Between, 100, 500));

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        restored.SimpleFilters[0].Operator.Should().Be(FilterOperator.Between);
        ((double)restored.SimpleFilters[0].Value!).Should().Be(100d);
        ((double)restored.SimpleFilters[0].ValueEnd!).Should().Be(500d);
    }

    [Fact]
    public void RoundTripAdvancedFilterWithNestedSubGroup()
    {
        var inner = new FilterGroup(
            FilterLogic.Or,
            new[]
            {
                new FilterCriterion("Status", FilterOperator.Equals, "Draft"),
                new FilterCriterion("Status", FilterOperator.Equals, "Review"),
            },
            Array.Empty<FilterGroup>());

        var outer = new FilterGroup(
            FilterLogic.And,
            new[] { new FilterCriterion("Amount", FilterOperator.GreaterThan, 100) },
            new[] { inner });

        var state = new GridFilterState { AdvancedFilter = outer };

        var restored = GridFilterStateSerializer.Deserialize(GridFilterStateSerializer.Serialize(state));

        restored.AdvancedFilter.Should().NotBeNull();
        restored.AdvancedFilter!.Logic.Should().Be(FilterLogic.And);
        restored.AdvancedFilter.Criteria.Should().HaveCount(1);
        restored.AdvancedFilter.Criteria[0].Field.Should().Be("Amount");
        restored.AdvancedFilter.SubGroups.Should().NotBeNull();
        restored.AdvancedFilter.SubGroups!.Should().HaveCount(1);
        restored.AdvancedFilter.SubGroups[0].Logic.Should().Be(FilterLogic.Or);
        restored.AdvancedFilter.SubGroups[0].Criteria.Should().HaveCount(2);
    }

    [Fact]
    public void RoundTripFullStateCombiningSourcesGfi16()
    {
        // GFI16: simple and advanced writes share AdvancedFilter. The flat
        // append semantics of AddSimpleFilter mean all three criteria live on
        // the same root AND group after Setup, and the round-trip must preserve
        // them in order.
        var state = new GridFilterState { GlobalSearch = "acme" };
        state.AddSimpleFilter(new FilterCriterion("Type", FilterOperator.Equals, "Customer"));
        state.AddSimpleFilter(new FilterCriterion("Balance", FilterOperator.GreaterThan, 1000));
        state.AddSimpleFilter(new FilterCriterion("Country", FilterOperator.Equals, "FR"));

        var json = GridFilterStateSerializer.Serialize(state);
        var restored = GridFilterStateSerializer.Deserialize(json);

        restored.GlobalSearch.Should().Be("acme");
        restored.AdvancedFilter.Should().NotBeNull();
        restored.AdvancedFilter!.Criteria.Should().HaveCount(3);
        restored.SimpleFilters.Should().HaveCount(3);
        restored.SimpleFilters[0].Field.Should().Be("Type");
        restored.SimpleFilters[1].Field.Should().Be("Balance");
        restored.SimpleFilters[2].Field.Should().Be("Country");
    }

    [Fact]
    public void RoundTripNestedStatePreservesSubGroupsAndRootCriteriaGfi16()
    {
        // GFI16 P1: a state with both root AND criteria and sub-groups must
        // survive the persistence round-trip intact, even though the URL path
        // can only carry the root criteria. The preference blob is the only
        // place where the sub-groups are stored, so Serialize/Deserialize has
        // to keep them whole.
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Country", FilterOperator.Equals, "FR"));
        var subGroup = new FilterGroup(
            FilterLogic.Or,
            new[]
            {
                new FilterCriterion("Status", FilterOperator.Equals, "Draft"),
                new FilterCriterion("Status", FilterOperator.Equals, "Review"),
            });
        state.AdvancedFilter = new FilterGroup(
            FilterLogic.And,
            state.AdvancedFilter!.Criteria,
            new[] { subGroup });

        var json = GridFilterStateSerializer.Serialize(state);
        var restored = GridFilterStateSerializer.Deserialize(json);

        restored.AdvancedFilter.Should().NotBeNull();
        restored.AdvancedFilter!.Criteria.Should().ContainSingle()
            .Which.Field.Should().Be("Country");
        restored.AdvancedFilter.SubGroups.Should().NotBeNull();
        restored.AdvancedFilter.SubGroups!.Should().HaveCount(1);
        restored.AdvancedFilter.SubGroups[0].Criteria.Should().HaveCount(2);
    }

    [Fact]
    public void RoundTripNestedStateWithGlobalSearchGfi16()
    {
        var state = new GridFilterState { GlobalSearch = "acme" };
        state.AddSimpleFilter(new FilterCriterion("Type", FilterOperator.Equals, "Customer"));

        // Escalate to a nested expression by injecting a sub-group directly.
        state.AdvancedFilter = new FilterGroup(
            FilterLogic.And,
            state.AdvancedFilter!.Criteria,
            new[]
            {
                new FilterGroup(
                    FilterLogic.Or,
                    new[]
                    {
                        new FilterCriterion("Status", FilterOperator.Equals, "Draft"),
                        new FilterCriterion("Status", FilterOperator.Equals, "Review"),
                    }),
            });

        var json = GridFilterStateSerializer.Serialize(state);
        var restored = GridFilterStateSerializer.Deserialize(json);

        restored.GlobalSearch.Should().Be("acme");
        restored.AdvancedFilter.Should().NotBeNull();
        restored.AdvancedFilter!.Criteria.Should().HaveCount(1);
        restored.AdvancedFilter.SubGroups.Should().NotBeNull();
        restored.AdvancedFilter.SubGroups!.Should().HaveCount(1);

        // GFI16: root AND criteria remain exposed as simple filters even when
        // sub-groups are also present — URL sync needs them to stay shareable.
        restored.SimpleFilters.Should().ContainSingle()
            .Which.Field.Should().Be("Type");
    }

    [Fact]
    public void DeserializeLegacyBlobMergesSimpleIntoAdvancedGfi16()
    {
        // GFI16 backward compatibility: pre-GFI16 blobs carried a "simple"
        // array next to the "advanced" group. The deserializer must merge
        // those flat criteria into the unified tree via AddSimpleFilter.
        const string legacyJson =
            "{\"q\":\"acme\",\"simple\":[{\"f\":\"Type\",\"op\":\"Equals\",\"vt\":\"s\",\"v\":\"Customer\"}],"
            + "\"advanced\":{\"logic\":\"And\",\"criteria\":[{\"f\":\"Country\",\"op\":\"Equals\",\"vt\":\"s\",\"v\":\"FR\"}]}}";

        var restored = GridFilterStateSerializer.Deserialize(legacyJson);

        restored.GlobalSearch.Should().Be("acme");
        restored.AdvancedFilter.Should().NotBeNull();
        restored.AdvancedFilter!.Criteria.Should().HaveCount(2);
        restored.AdvancedFilter.Criteria.Select(c => c.Field).Should().BeEquivalentTo(LegacyMergedFieldsInOrder);
    }

    [Fact]
    public void SerializeShouldProduceCompactShapeForHumanInspectionGfi16()
    {
        // Sanity check that the jsonb blob uses the compact keys defined by the DTO
        // (small persisted footprint + predictable shape for potential manual queries).
        // Post-GFI16 there is no longer a "simple" array — all filters live under
        // "advanced".
        var state = new GridFilterState { GlobalSearch = "abc" };
        state.AddSimpleFilter(new FilterCriterion("Name", FilterOperator.Contains, "Acme"));

        var json = GridFilterStateSerializer.Serialize(state);

        json.Should().Contain("\"q\":\"abc\"");
        json.Should().Contain("\"advanced\":");
        json.Should().Contain("\"criteria\":");
        json.Should().Contain("\"f\":\"Name\"");
        json.Should().Contain("\"op\":\"Contains\"");
        json.Should().Contain("\"vt\":\"s\"");
        json.Should().NotContain("\"simple\":");
    }
}

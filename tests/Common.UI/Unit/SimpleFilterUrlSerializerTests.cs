namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

/// <summary>
/// GFI14 — Round-trip + edge cases for <see cref="SimpleFilterUrlSerializer"/>.
/// Ensures simple filter state can be shared via the URL query string and rebuilt
/// on arrival without crashing on malformed input.
/// </summary>
public sealed class SimpleFilterUrlSerializerTests
{
    [Fact]
    public void SerializeShouldReturnEmptyStringWhenNoSimpleFilters()
    {
        var state = new GridFilterState();
        SimpleFilterUrlSerializer.Serialize(state).Should().BeEmpty();
    }

    [Fact]
    public void SerializeShouldEncodeEqualsCriterion()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Service", FilterOperator.Equals, "finances"));

        var encoded = SimpleFilterUrlSerializer.Serialize(state);

        encoded.Should().Be("filter=Service%3Aeq%3Afinances");
    }

    [Fact]
    public void SerializeShouldEncodeMultipleCriteriaSeparatedByAmpersand()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Service", FilterOperator.Equals, "finances"));
        state.AddSimpleFilter(new FilterCriterion("Amount", FilterOperator.GreaterThan, 5000));

        var encoded = SimpleFilterUrlSerializer.Serialize(state);

        encoded.Should().Contain("filter=Service%3Aeq%3Afinances");
        encoded.Should().Contain("filter=Amount%3Agt%3A5000");
        encoded.Should().Contain("&");
    }

    [Fact]
    public void SerializeShouldEncodeBetweenCriterionWithPipeSeparator()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Amount", FilterOperator.Between, 100, 500));

        var encoded = SimpleFilterUrlSerializer.Serialize(state);
        var parsed = SimpleFilterUrlSerializer.Parse(encoded);

        parsed.Should().HaveCount(1);
        parsed[0].Operator.Should().Be(FilterOperator.Between);
        parsed[0].Value.Should().Be("100");
        parsed[0].ValueEnd.Should().Be("500");
    }

    [Fact]
    public void SerializeShouldEncodeNullOperatorWithEmptyValue()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Notes", FilterOperator.IsNull, Value: null));

        var encoded = SimpleFilterUrlSerializer.Serialize(state);
        var parsed = SimpleFilterUrlSerializer.Parse(encoded);

        parsed.Should().HaveCount(1);
        parsed[0].Field.Should().Be("Notes");
        parsed[0].Operator.Should().Be(FilterOperator.IsNull);
        parsed[0].Value.Should().BeNull();
    }

    [Fact]
    public void SerializeShouldEncodeDateOnlyAsIsoFormat()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(
            new FilterCriterion("StartDate", FilterOperator.GreaterThanOrEqual, new DateOnly(2026, 1, 15)));

        var encoded = SimpleFilterUrlSerializer.Serialize(state);
        var parsed = SimpleFilterUrlSerializer.Parse(encoded);

        parsed[0].Value.Should().Be("2026-01-15");
    }

    [Fact]
    public void SerializeShouldUseInvariantCultureForDecimalNumbers()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Price", FilterOperator.LessThan, 12.5m));

        var encoded = SimpleFilterUrlSerializer.Serialize(state);
        var parsed = SimpleFilterUrlSerializer.Parse(encoded);

        // Invariant culture → decimal point, not comma.
        parsed[0].Value.Should().Be("12.5");
    }

    [Theory]
    [InlineData(FilterOperator.Equals, "eq")]
    [InlineData(FilterOperator.NotEquals, "neq")]
    [InlineData(FilterOperator.Contains, "ct")]
    [InlineData(FilterOperator.NotContains, "nct")]
    [InlineData(FilterOperator.StartsWith, "sw")]
    [InlineData(FilterOperator.EndsWith, "ew")]
    [InlineData(FilterOperator.GreaterThan, "gt")]
    [InlineData(FilterOperator.GreaterThanOrEqual, "gte")]
    [InlineData(FilterOperator.LessThan, "lt")]
    [InlineData(FilterOperator.LessThanOrEqual, "lte")]
    [InlineData(FilterOperator.Between, "bt")]
    [InlineData(FilterOperator.NotBetween, "nbt")]
    [InlineData(FilterOperator.Before, "before")]
    [InlineData(FilterOperator.After, "after")]
    [InlineData(FilterOperator.In, "in")]
    [InlineData(FilterOperator.NotIn, "nin")]
    [InlineData(FilterOperator.IsNull, "null")]
    [InlineData(FilterOperator.IsNotNull, "nnull")]
    [InlineData(FilterOperator.RelativePeriod, "rel")]
    public void AllOperatorsShouldRoundTripThroughTheirCompactCode(FilterOperator op, string expectedCode)
    {
        var state = new GridFilterState();
        FilterCriterion criterion = op switch
        {
            FilterOperator.Between or FilterOperator.NotBetween => new FilterCriterion("F", op, "a", "b"),
            FilterOperator.IsNull or FilterOperator.IsNotNull => new FilterCriterion("F", op, Value: null),

            // Use a real list for In/NotIn so the theory actually exercises the
            // IEnumerable encoder/decoder path and cannot degenerate into a scalar
            // false-green like it did before the round-2 fix.
            FilterOperator.In or FilterOperator.NotIn => new FilterCriterion("F", op, new List<string> { "x", "y" }),
            _ => new FilterCriterion("F", op, "x"),
        };
        state.AddSimpleFilter(criterion);

        var encoded = SimpleFilterUrlSerializer.Serialize(state);
        encoded.Should().Contain(expectedCode);

        var parsed = SimpleFilterUrlSerializer.Parse(encoded);
        parsed.Should().HaveCount(1);
        parsed[0].Operator.Should().Be(op);

        if (op is FilterOperator.In or FilterOperator.NotIn)
        {
            // The decoder must return a real IEnumerable<string>, not a plain
            // string carrying the comma-joined representation — otherwise the
            // expression builder would treat it as IEnumerable<char>.
            parsed[0].Value.Should().BeAssignableTo<System.Collections.Generic.IEnumerable<string>>();
            ((System.Collections.Generic.IEnumerable<string>)parsed[0].Value!).Should().Equal("x", "y");
        }
    }

    [Fact]
    public void SerializeShouldEncodeInCriterionAsCommaJoinedList()
    {
        // GFI14 round-2 fix: In/NotIn values are IEnumerable. Before the fix
        // FormatValue fell through to .ToString() and emitted the CLR type
        // name, making list-based filters un-shareable.
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion(
            "Status",
            FilterOperator.In,
            new List<string> { "Draft", "Review", "Published" }));

        var encoded = SimpleFilterUrlSerializer.Serialize(state);

        encoded.Should().Contain("filter=Status%3Ain%3ADraft%2CReview%2CPublished");
        encoded.Should().NotContain("System.Collections");
    }

    [Fact]
    public void RoundTripInCriterionWithListValue()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion(
            "Status",
            FilterOperator.In,
            new List<string> { "Draft", "Review", "Published" }));

        var encoded = SimpleFilterUrlSerializer.Serialize(state);
        var parsed = SimpleFilterUrlSerializer.Parse(encoded);

        parsed.Should().HaveCount(1);
        parsed[0].Field.Should().Be("Status");
        parsed[0].Operator.Should().Be(FilterOperator.In);

        // Value must decode to a real IEnumerable — FilterExpressionBuilder.BuildIn
        // rejects a plain string ("In operator requires an IEnumerable value").
        var value = parsed[0].Value;
        value.Should().BeAssignableTo<System.Collections.Generic.IEnumerable<string>>();
        var items = ((System.Collections.Generic.IEnumerable<string>)value!).ToList();
        items.Should().Equal("Draft", "Review", "Published");
    }

    [Fact]
    public void RoundTripNotInCriterionWithIntegerListValue()
    {
        // Numeric list items are stringified with the invariant culture; the
        // expression builder coerces each item via ConvertConstant on restore.
        var priorities = new List<int> { 1, 2, 3 };
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion(
            "Priority",
            FilterOperator.NotIn,
            priorities));

        var encoded = SimpleFilterUrlSerializer.Serialize(state);
        var parsed = SimpleFilterUrlSerializer.Parse(encoded);

        parsed.Should().HaveCount(1);
        parsed[0].Operator.Should().Be(FilterOperator.NotIn);
        var items = ((System.Collections.Generic.IEnumerable<string>)parsed[0].Value!).ToList();
        items.Should().Equal("1", "2", "3");
    }

    [Fact]
    public void ParseShouldTolerateLeadingQuestionMark()
    {
        var parsed = SimpleFilterUrlSerializer.Parse("?filter=Service%3Aeq%3Afinances");
        parsed.Should().HaveCount(1);
        parsed[0].Field.Should().Be("Service");
    }

    [Fact]
    public void ParseShouldReturnEmptyForNullOrBlank()
    {
        SimpleFilterUrlSerializer.Parse(null).Should().BeEmpty();
        SimpleFilterUrlSerializer.Parse(string.Empty).Should().BeEmpty();
        SimpleFilterUrlSerializer.Parse("?").Should().BeEmpty();
    }

    [Fact]
    public void ParseShouldSkipMalformedEntries()
    {
        // Garbled segments must not crash the page.
        var parsed = SimpleFilterUrlSerializer.Parse(
            "?filter=not-a-valid-triple&filter=Service%3Aeq%3Afinances&filter=%3A%3A&filter=Amount%3Aunknown%3A5");

        parsed.Should().HaveCount(1);
        parsed[0].Field.Should().Be("Service");
    }

    [Fact]
    public void ParseShouldIgnoreNonFilterQueryParameters()
    {
        var parsed = SimpleFilterUrlSerializer.Parse("?page=2&filter=Service%3Aeq%3Afinances&sort=Name");
        parsed.Should().HaveCount(1);
        parsed[0].Field.Should().Be("Service");
    }

    [Fact]
    public void ParseShouldReturnEmptyWhenBetweenIsMissingPipe()
    {
        var parsed = SimpleFilterUrlSerializer.Parse("?filter=Amount%3Abt%3A100");
        parsed.Should().BeEmpty();
    }

    [Fact]
    public void ParseShouldSupportValuesContainingColons()
    {
        // A value like "12:30" must survive — the splitter only cuts on the first two colons.
        var parsed = SimpleFilterUrlSerializer.Parse("?filter=StartTime%3Aeq%3A12%3A30");
        parsed.Should().HaveCount(1);
        parsed[0].Value.Should().Be("12:30");
    }

    [Fact]
    public void BuildUriWithFiltersShouldPreserveNonFilterQueryParameters()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Service", FilterOperator.Equals, "finances"));

        var rebuilt = SimpleFilterUrlSerializer.BuildUriWithFilters(
            "https://app.example/party?page=3&sort=Name", state);

        rebuilt.Should().StartWith("https://app.example/party?");
        rebuilt.Should().Contain("page=3");
        rebuilt.Should().Contain("sort=Name");
        rebuilt.Should().Contain("filter=Service%3Aeq%3Afinances");
    }

    [Fact]
    public void BuildUriWithFiltersShouldReplaceAnyPreviousFilterParameters()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Amount", FilterOperator.GreaterThan, 1000));

        var rebuilt = SimpleFilterUrlSerializer.BuildUriWithFilters(
            "https://app.example/invoices?filter=Old%3Aeq%3Avalue&page=2", state);

        rebuilt.Should().Contain("page=2");
        rebuilt.Should().Contain("filter=Amount%3Agt%3A1000");
        rebuilt.Should().NotContain("filter=Old");
    }

    [Fact]
    public void BuildUriWithFiltersShouldStripFilterParamsWhenStateHasNoSimpleFilters()
    {
        var state = new GridFilterState();

        var rebuilt = SimpleFilterUrlSerializer.BuildUriWithFilters(
            "https://app.example/invoices?filter=Old%3Aeq%3Avalue&page=2", state);

        rebuilt.Should().Be("https://app.example/invoices?page=2");
    }

    [Fact]
    public void BuildUriWithFiltersShouldPreserveHashFragment()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("X", FilterOperator.Equals, "1"));

        var rebuilt = SimpleFilterUrlSerializer.BuildUriWithFilters(
            "https://app.example/page?a=1#section", state);

        rebuilt.Should().EndWith("#section");
    }

    [Fact]
    public void BuildUriWithFiltersShouldReturnBarePathWhenNoQueryAndNoFilters()
    {
        var state = new GridFilterState();
        var rebuilt = SimpleFilterUrlSerializer.BuildUriWithFilters("https://app.example/page", state);
        rebuilt.Should().Be("https://app.example/page");
    }

    [Fact]
    public void ApplyToStateShouldReplaceOnlySimpleFiltersGfi16()
    {
        // GFI16: "only simple filters" means "only the flat root criteria of
        // the unified tree". Any sub-groups (nested advanced expression) must
        // survive the URL re-application. We stage a nested state here and
        // verify the sub-group is preserved while a stale root criterion is
        // replaced by the URL entry.
        var subGroup = new FilterGroup(
            FilterLogic.Or,
            new[]
            {
                new FilterCriterion("Status", FilterOperator.Equals, "Draft"),
                new FilterCriterion("Status", FilterOperator.Equals, "Review"),
            });
        var state = new GridFilterState
        {
            GlobalSearch = "abc",
            AdvancedFilter = new FilterGroup(
                FilterLogic.And,
                new[] { new FilterCriterion("Stale", FilterOperator.Equals, "old") },
                new[] { subGroup }),
        };

        SimpleFilterUrlSerializer.ApplyToState(state, "?filter=Amount%3Agt%3A1000");

        state.GlobalSearch.Should().Be("abc");
        state.AdvancedFilter.Should().NotBeNull();
        state.AdvancedFilter!.Criteria.Should().ContainSingle()
            .Which.Field.Should().Be("Amount");
        state.AdvancedFilter.SubGroups.Should().NotBeNull();
        state.AdvancedFilter.SubGroups!.Should().ContainSingle().Which.Should().Be(subGroup);
    }

    [Fact]
    public void SerializeShouldNotLeakFlatOrRootIntoUrlGfi16()
    {
        // GFI16 P1: a flat OR root must not surface in the URL. If it did,
        // SimpleFilterUrlSerializer.Parse would rebuild each criterion via
        // AddSimpleFilter, which produces a flat AND — silently flipping the
        // semantics of the shared filter.
        var state = new GridFilterState
        {
            AdvancedFilter = new FilterGroup(
                FilterLogic.Or,
                new[]
                {
                    new FilterCriterion("Status", FilterOperator.Equals, "Draft"),
                    new FilterCriterion("Status", FilterOperator.Equals, "Review"),
                }),
        };

        var encoded = SimpleFilterUrlSerializer.Serialize(state);

        encoded.Should().BeEmpty();
    }

    [Fact]
    public void SerializeShouldReturnEmptyWhenAdvancedFilterHasSubGroupsGfi16()
    {
        // GFI16 P1/P2: when the advanced filter has sub-groups, the URL must
        // stay empty. Publishing only the root criteria would make shared
        // deep links non-deterministic (the receiver's saved advanced branch
        // would silently merge in) AND would silently drop the sub-groups on
        // a share-link round trip. Keeping the URL empty forces the persisted
        // blob to restore the full state on refresh.
        var state = new GridFilterState
        {
            AdvancedFilter = new FilterGroup(
                FilterLogic.And,
                new[] { new FilterCriterion("Amount", FilterOperator.GreaterThan, 1000) },
                new[]
                {
                    new FilterGroup(
                        FilterLogic.Or,
                        new[]
                        {
                            new FilterCriterion("Status", FilterOperator.Equals, "Draft"),
                            new FilterCriterion("Status", FilterOperator.Equals, "Review"),
                        }),
                }),
        };

        var encoded = SimpleFilterUrlSerializer.Serialize(state);

        encoded.Should().BeEmpty();
    }

    [Fact]
    public void ApplyToStateShouldClearSimpleFiltersWhenQueryIsEmpty()
    {
        var state = new GridFilterState();
        state.AddSimpleFilter(new FilterCriterion("Stale", FilterOperator.Equals, "old"));

        SimpleFilterUrlSerializer.ApplyToState(state, query: null);

        state.SimpleFilters.Should().BeEmpty();
    }
}

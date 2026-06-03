namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using System.Text.Json;
using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

/// <summary>
/// Verifies FilterGroup JSON serialization round-trip,
/// matching the storage format used by <c>PostgresSavedFilterService</c>.
/// </summary>
public sealed class SavedFilterSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void SimpleFilterGroupShouldRoundTrip()
    {
        var group = new FilterGroup(
            FilterLogic.And,
            [
                new FilterCriterion("Name", FilterOperator.Contains, "test"),
                new FilterCriterion("Amount", FilterOperator.GreaterThan, 500),
            ]);

        var json = JsonSerializer.Serialize(group, Options);
        var deserialized = JsonSerializer.Deserialize<FilterGroup>(json, Options);

        deserialized.Should().NotBeNull();
        deserialized!.Logic.Should().Be(FilterLogic.And);
        deserialized.Criteria.Should().HaveCount(2);
        deserialized.Criteria[0].Field.Should().Be("Name");
        deserialized.Criteria[0].Operator.Should().Be(FilterOperator.Contains);
        deserialized.Criteria[1].Field.Should().Be("Amount");
    }

    [Fact]
    public void NestedFilterGroupShouldRoundTrip()
    {
        var inner = new FilterGroup(
            FilterLogic.Or,
            [
                new FilterCriterion("Status", FilterOperator.Equals, "Active"),
                new FilterCriterion("Status", FilterOperator.Equals, "Pending"),
            ]);

        var group = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("Amount", FilterOperator.GreaterThan, 100)],
            [inner]);

        var json = JsonSerializer.Serialize(group, Options);
        var deserialized = JsonSerializer.Deserialize<FilterGroup>(json, Options);

        deserialized.Should().NotBeNull();
        deserialized!.SubGroups.Should().HaveCount(1);
        deserialized.SubGroups![0].Logic.Should().Be(FilterLogic.Or);
        deserialized.SubGroups[0].Criteria.Should().HaveCount(2);
    }

    [Fact]
    public void EmptyFilterGroupShouldRoundTrip()
    {
        var group = new FilterGroup(FilterLogic.And, []);

        var json = JsonSerializer.Serialize(group, Options);
        var deserialized = JsonSerializer.Deserialize<FilterGroup>(json, Options);

        deserialized.Should().NotBeNull();
        deserialized!.Logic.Should().Be(FilterLogic.And);
        deserialized.Criteria.Should().BeEmpty();
        deserialized.SubGroups.Should().BeNull();
    }

    [Fact]
    public void BetweenOperatorShouldPreserveValueEnd()
    {
        var group = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("Amount", FilterOperator.Between, 100, 500)]);

        var json = JsonSerializer.Serialize(group, Options);
        var deserialized = JsonSerializer.Deserialize<FilterGroup>(json, Options);

        deserialized.Should().NotBeNull();
        var criterion = deserialized!.Criteria[0];
        criterion.Operator.Should().Be(FilterOperator.Between);
    }

    [Fact]
    public void CamelCaseSerializationShouldProduceExpectedKeys()
    {
        var group = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("Name", FilterOperator.Equals, "test")]);

        var json = JsonSerializer.Serialize(group, Options);

        json.Should().Contain("\"logic\":");
        json.Should().Contain("\"criteria\":");
        json.Should().Contain("\"field\":");
        json.Should().Contain("\"operator\":");
    }

    [Fact]
    public void RelatedTableFieldsShouldRoundTrip()
    {
        var group = new FilterGroup(
            FilterLogic.And,
            [
                new FilterCriterion("Customer.City", FilterOperator.Equals, "Paris"),
                new FilterCriterion("Customer.Country", FilterOperator.Contains, "FR"),
            ]);

        var json = JsonSerializer.Serialize(group, Options);
        var deserialized = JsonSerializer.Deserialize<FilterGroup>(json, Options);

        deserialized.Should().NotBeNull();
        deserialized!.Criteria[0].Field.Should().Be("Customer.City");
        deserialized.Criteria[1].Field.Should().Be("Customer.Country");
    }
}

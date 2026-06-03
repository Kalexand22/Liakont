namespace Stratum.Common.Abstractions.Tests.Unit;

using System.Text.Json;
using FluentAssertions;
using Stratum.Common.Abstractions.Queries;
using Xunit;

public sealed class ListQueryTests
{
    [Fact]
    public void NewShouldUseDefaultValues()
    {
        var query = new ListQuery();

        query.Search.Should().BeNull();
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(25);
        query.SortField.Should().BeNull();
        query.SortDirection.Should().Be(SortDirection.Ascending);
        query.Filters.Should().BeNull();
    }

    [Fact]
    public void NewShouldAcceptAllPropertiesWhenAllFieldsSet()
    {
        var filters = new Dictionary<string, string> { ["status"] = "active" }
            .AsReadOnly();

        var query = new ListQuery
        {
            Search = "test",
            Page = 3,
            PageSize = 50,
            SortField = "Name",
            SortDirection = SortDirection.Descending,
            Filters = filters,
        };

        query.Search.Should().Be("test");
        query.Page.Should().Be(3);
        query.PageSize.Should().Be(50);
        query.SortField.Should().Be("Name");
        query.SortDirection.Should().Be(SortDirection.Descending);
        query.Filters.Should().ContainKey("status").WhoseValue.Should().Be("active");
    }

    [Fact]
    public void JsonSerializationShouldRoundtripAllProperties()
    {
        var original = new ListQuery
        {
            Search = "hello",
            Page = 2,
            PageSize = 10,
            SortField = "Date",
            SortDirection = SortDirection.Descending,
            Filters = new Dictionary<string, string> { ["type"] = "invoice" }.AsReadOnly(),
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ListQuery>(json);

        deserialized.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void EqualsShouldReturnTrueWhenScalarPropertiesMatch()
    {
        var a = new ListQuery { Page = 2, PageSize = 10 };
        var b = new ListQuery { Page = 2, PageSize = 10 };

        a.Should().Be(b);
    }

    [Fact]
    public void EqualsShouldUsReferenceEqualityForFilters()
    {
        var filtersA = new Dictionary<string, string> { ["status"] = "active" }.AsReadOnly();
        var filtersB = new Dictionary<string, string> { ["status"] = "active" }.AsReadOnly();

        var a = new ListQuery { Filters = filtersA };
        var b = new ListQuery { Filters = filtersA };
        var c = new ListQuery { Filters = filtersB };

        // Same reference → equal.
        a.Should().Be(b);

        // Different reference with same contents → not equal (by design, documented in XML doc).
        a.Should().NotBe(c);
    }

    [Fact]
    public void WithExpressionShouldProduceModifiedCopy()
    {
        var original = new ListQuery { Page = 1 };
        var modified = original with { Page = 5 };

        modified.Page.Should().Be(5);
        original.Page.Should().Be(1);
    }
}

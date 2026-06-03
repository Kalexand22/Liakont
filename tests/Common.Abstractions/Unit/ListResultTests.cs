namespace Stratum.Common.Abstractions.Tests.Unit;

using System.Text.Json;
using FluentAssertions;
using Stratum.Common.Abstractions.Queries;
using Xunit;

public sealed class ListResultTests
{
    [Fact]
    public void NewShouldUseDefaultValues()
    {
        var result = new ListResult<string>();

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public void NewShouldAcceptItemsAndCount()
    {
        var result = new ListResult<int>
        {
            Items = [1, 2, 3],
            TotalCount = 100,
        };

        result.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(100);
    }

    [Fact]
    public void JsonSerializationShouldRoundtripAllProperties()
    {
        var original = new ListResult<string>
        {
            Items = ["a", "b", "c"],
            TotalCount = 42,
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ListResult<string>>(json);

        deserialized.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void EqualsShouldReturnTrueWithSameItemsReference()
    {
        var itemsA = new List<string> { "x" }.AsReadOnly();
        var itemsB = new List<string> { "x" }.AsReadOnly();
        var a = new ListResult<string> { Items = itemsA, TotalCount = 1 };
        var b = new ListResult<string> { Items = itemsA, TotalCount = 1 };

        // Record equality uses reference equality for IReadOnlyList<T>.
        // Same reference → equal.
        a.Should().Be(b);

        // Different reference with same contents → not equal (by design).
        var c = new ListResult<string> { Items = itemsB, TotalCount = 1 };
        a.Should().NotBe(c);
    }
}

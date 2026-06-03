namespace Stratum.Common.Infrastructure.Tests.Unit.Caching;

using FluentAssertions;
using Stratum.Common.Infrastructure.Caching;
using Xunit;

/// <summary>
/// Unit tests for <see cref="InMemoryCacheService"/> (via internal access).
/// Covers get miss/hit, passive expiry, overwrites, prefix removal, and guard clauses.
/// </summary>
public sealed class InMemoryCacheServiceTests
{
    [Fact]
    public async Task GetAsync_Should_ReturnNull_When_KeyIsAbsent()
    {
        var cache = CreateCache();

        var result = await cache.GetAsync<string>("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_Should_ReturnValue_When_KeyIsPresent()
    {
        var cache = CreateCache();
        await cache.SetAsync("k", "hello", TimeSpan.FromMinutes(5));

        var result = await cache.GetAsync<string>("k");

        result.Should().Be("hello");
    }

    [Fact]
    public async Task GetAsync_Should_ReturnNull_When_EntryHasExpired()
    {
        var cache = CreateCache();
        await cache.SetAsync("k", "value", TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        var result = await cache.GetAsync<string>("k");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_Should_OverwriteExistingEntry_When_KeyAlreadyExists()
    {
        var cache = CreateCache();
        await cache.SetAsync("k", "first", TimeSpan.FromMinutes(5));
        await cache.SetAsync("k", "second", TimeSpan.FromMinutes(5));

        var result = await cache.GetAsync<string>("k");

        result.Should().Be("second");
    }

    [Fact]
    public async Task RemoveAsync_Should_DeleteEntry_When_KeyExists()
    {
        var cache = CreateCache();
        await cache.SetAsync("k", "value", TimeSpan.FromMinutes(5));

        await cache.RemoveAsync("k");

        var result = await cache.GetAsync<string>("k");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_Should_NotThrow_When_KeyIsAbsent()
    {
        var cache = CreateCache();

        var act = () => cache.RemoveAsync("nonexistent");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_Should_RemoveMatchingEntries_When_PrefixMatches()
    {
        var cache = CreateCache();
        await cache.SetAsync("user:1", "a", TimeSpan.FromMinutes(5));
        await cache.SetAsync("user:2", "b", TimeSpan.FromMinutes(5));
        await cache.SetAsync("order:1", "c", TimeSpan.FromMinutes(5));

        await cache.RemoveByPrefixAsync("user:");

        (await cache.GetAsync<string>("user:1")).Should().BeNull();
        (await cache.GetAsync<string>("user:2")).Should().BeNull();
        (await cache.GetAsync<string>("order:1")).Should().Be("c");
    }

    [Fact]
    public async Task SetAsync_Should_ThrowArgumentOutOfRangeException_When_TtlIsZero()
    {
        var cache = CreateCache();

        var act = () => cache.SetAsync("k", "v", TimeSpan.Zero);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("ttl");
    }

    [Fact]
    public async Task SetAsync_Should_ThrowArgumentOutOfRangeException_When_TtlIsNegative()
    {
        var cache = CreateCache();

        var act = () => cache.SetAsync("k", "v", TimeSpan.FromSeconds(-1));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("ttl");
    }

    [Fact]
    public async Task RemoveByPrefixAsync_Should_ThrowArgumentException_When_PrefixIsEmpty()
    {
        var cache = CreateCache();

        var act = () => cache.RemoveByPrefixAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("prefix");
    }

    private static InMemoryCacheService CreateCache() => new();
}

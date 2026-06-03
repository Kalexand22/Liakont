namespace Stratum.Common.Infrastructure.Tests.Integration.GridPreferences;

using Dapper;
using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Infrastructure.GridPreferences;
using Stratum.Common.Testing;
using Xunit;

/// <summary>
/// Integration tests for <see cref="PostgresSavedFilterService"/>.
/// Focused on the GFI10 <c>Source</c> column roundtrip so a Dapper-mapping
/// regression or a dropped default would break the build, not ship silently.
/// Ref: V013__add_source_to_saved_filters.sql, DF-02.
/// </summary>
public sealed class PostgresSavedFilterServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public PostgresSavedFilterServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveAsync_WithAdvancedSource_RoundTripsAsAdvanced()
    {
        await ClearTableAsync();
        var service = new PostgresSavedFilterService(_fixture.CreateConnectionFactory());
        var original = BuildFilter(source: SavedFilterSource.Advanced, name: "Advanced filter");

        var saved = await service.SaveAsync(original);
        saved.Source.Should().Be(
            SavedFilterSource.Advanced,
            "SaveAsync must return the same Source it wrote via RETURNING");

        var fetched = await service.GetAsync(saved.Id);
        fetched.Should().NotBeNull();
        fetched!.Source.Should().Be(
            SavedFilterSource.Advanced,
            "GetAsync must read back the Source column verbatim");
    }

    [Fact]
    public async Task SaveAsync_WithSimpleSource_RoundTripsAsSimple()
    {
        await ClearTableAsync();
        var service = new PostgresSavedFilterService(_fixture.CreateConnectionFactory());
        var original = BuildFilter(source: SavedFilterSource.Simple, name: "Simple filter");

        var saved = await service.SaveAsync(original);
        saved.Source.Should().Be(
            SavedFilterSource.Simple,
            "SaveAsync must preserve Source.Simple through the INSERT/RETURNING roundtrip");

        var fetched = await service.GetAsync(saved.Id);
        fetched.Should().NotBeNull();
        fetched!.Source.Should().Be(
            SavedFilterSource.Simple,
            "GetAsync must read back Source.Simple — DF-02 relies on this for restoration");
    }

    [Fact]
    public async Task ListAsync_ReturnsAllSourcesInSingleCall()
    {
        await ClearTableAsync();
        var service = new PostgresSavedFilterService(_fixture.CreateConnectionFactory());
        var userId = Guid.NewGuid();
        const string gridKey = "Test.GFI10.MixedSources";

        await service.SaveAsync(BuildFilter(
            source: SavedFilterSource.Advanced, name: "Adv", userId: userId, gridKey: gridKey));
        await service.SaveAsync(BuildFilter(
            source: SavedFilterSource.Simple, name: "Simp", userId: userId, gridKey: gridKey));

        var all = await service.ListAsync(userId, gridKey);

        all.Should().HaveCount(2);
        all.Should().Contain(f => f.Source == SavedFilterSource.Advanced);
        all.Should().Contain(f => f.Source == SavedFilterSource.Simple);
    }

    private static SavedFilter BuildFilter(
        SavedFilterSource source,
        string name,
        Guid? userId = null,
        string gridKey = "Test.GFI10.SourceRoundTrip")
    {
        var group = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("Name", FilterOperator.Contains, "x")]);

        return new SavedFilter(
            Id: Guid.NewGuid(),
            UserId: userId ?? Guid.NewGuid(),
            GridKey: gridKey,
            Name: name,
            FilterGroup: group,
            IsDefault: false,
            SharedWith: SharedScope.None,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Source: source);
    }

    private async Task ClearTableAsync()
    {
        using var conn = await _fixture.CreateConnectionFactory().OpenAsync();
        await conn.ExecuteAsync("DELETE FROM grid.saved_filters");
    }
}

namespace Stratum.Common.Infrastructure.Tests.Integration;

using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Infrastructure.Audit;
using Stratum.Common.Testing;
using Xunit;

public sealed class AuditWriterTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public AuditWriterTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WriteChangeAsyncShouldInsertRowWhenValidChange()
    {
        await ClearTableAsync();
        var entryId = Guid.NewGuid();
        var writer = new AuditWriter(_fixture.CreateConnectionFactory(), NullLogger<AuditWriter>.Instance);

        await writer.WriteChangeAsync(entryId, "Party", "party-1", "name", "Old Name", "New Name", "user-42");

        var rows = await QueryFieldChangesAsync(entryId);
        rows.Should().ContainSingle();
        rows[0].FieldName.Should().Be("name");
        rows[0].ActorId.Should().Be("user-42");
        rows[0].EntityType.Should().Be("Party");
        rows[0].EntityId.Should().Be("party-1");
    }

    [Fact]
    public async Task WriteChangeAsyncShouldStoreNullOldValueWhenCreateScenario()
    {
        await ClearTableAsync();
        var entryId = Guid.NewGuid();
        var writer = new AuditWriter(_fixture.CreateConnectionFactory(), NullLogger<AuditWriter>.Instance);

        await writer.WriteChangeAsync(entryId, "Party", "party-2", "name", oldValue: null, newValue: "Created Name", actorId: "system");

        var rows = await QueryFieldChangesAsync(entryId);
        rows.Should().ContainSingle();
        rows[0].OldValue.Should().BeNull();
        rows[0].NewValue.Should().NotBeNull();
    }

    [Fact]
    public async Task WriteChangeAsyncShouldGroupChangesByEntryId()
    {
        await ClearTableAsync();
        var entryId = Guid.NewGuid();
        var writer = new AuditWriter(_fixture.CreateConnectionFactory(), NullLogger<AuditWriter>.Instance);

        await writer.WriteChangeAsync(entryId, "Party", "party-3", "name", "Old", "New", "user-1");
        await writer.WriteChangeAsync(entryId, "Party", "party-3", "status", "Active", "Inactive", "user-1");

        var rows = await QueryFieldChangesAsync(entryId);
        rows.Should().HaveCount(2);
        rows.Select(r => r.FieldName).Should().BeEquivalentTo(["name", "status"]);
    }

    private async Task ClearTableAsync()
    {
        using var connection = await _fixture.CreateConnectionFactory().OpenAsync();
        await connection.ExecuteAsync("DELETE FROM audit.field_changes");
    }

    private async Task<IReadOnlyList<FieldChangeRow>> QueryFieldChangesAsync(Guid entryId)
    {
        using var connection = await _fixture.CreateConnectionFactory().OpenAsync();
        var rows = await connection.QueryAsync<FieldChangeRow>(
            """
            SELECT entry_id    AS "EntryId",
                   entity_type AS "EntityType",
                   entity_id   AS "EntityId",
                   field_name  AS "FieldName",
                   old_value   AS "OldValue",
                   new_value   AS "NewValue",
                   actor_id    AS "ActorId"
            FROM audit.field_changes
            WHERE entry_id = @EntryId
            ORDER BY occurred_at
            """,
            new { EntryId = entryId });
        return rows.AsList();
    }

    private sealed record FieldChangeRow
    {
        public Guid EntryId { get; init; }

        public string EntityType { get; init; } = string.Empty;

        public string EntityId { get; init; } = string.Empty;

        public string FieldName { get; init; } = string.Empty;

        public string? OldValue { get; init; }

        public string? NewValue { get; init; }

        public string ActorId { get; init; } = string.Empty;
    }
}

namespace Stratum.Common.Infrastructure.Tests.Integration;

using FluentAssertions;
using Stratum.Common.Testing;
using Xunit;

public sealed class MigrationTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public MigrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MigrationShouldCreateOutboxSchema()
    {
        var factory = _fixture.CreateConnectionFactory();
        using var connection = await factory.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'outbox')";
        var exists = (bool)command.ExecuteScalar()!;

        exists.Should().BeTrue("V001 migration creates the outbox schema");
    }

    [Fact]
    public async Task MigrationShouldCreateJournalTable()
    {
        var factory = _fixture.CreateConnectionFactory();
        using var connection = await factory.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'outbox' AND table_name = 'schema_versions'
            )
            """;
        var exists = (bool)command.ExecuteScalar()!;

        exists.Should().BeTrue("DbUp journal table should exist in outbox schema");
    }

    [Fact]
    public async Task MigrationV001ShouldBeRecordedInJournal()
    {
        var factory = _fixture.CreateConnectionFactory();
        using var connection = await factory.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM outbox.schema_versions
            WHERE scriptname LIKE '%V001__create_outbox_schema%'
            """;
        long count = (long)command.ExecuteScalar()!;

        count.Should().Be(1, "V001 migration script should be recorded in the journal table");
    }

    [Fact]
    public async Task MigrationV003ShouldCreateDeadLetterEventsTable()
    {
        var factory = _fixture.CreateConnectionFactory();
        using var connection = await factory.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'outbox' AND table_name = 'dead_letter_events'
            )
            """;
        var exists = (bool)command.ExecuteScalar()!;

        exists.Should().BeTrue("V003 migration creates the outbox.dead_letter_events table");
    }

    [Fact]
    public async Task MigrationV003ShouldAddRetryCountColumnToPendingEvents()
    {
        var factory = _fixture.CreateConnectionFactory();
        using var connection = await factory.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'outbox'
                  AND table_name   = 'pending_events'
                  AND column_name  = 'retry_count'
            )
            """;
        var exists = (bool)command.ExecuteScalar()!;

        exists.Should().BeTrue("V003 migration adds retry_count to outbox.pending_events");
    }

    [Fact]
    public async Task MigrationV003ShouldBeRecordedInJournal()
    {
        var factory = _fixture.CreateConnectionFactory();
        using var connection = await factory.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM outbox.schema_versions
            WHERE scriptname LIKE '%V003__outbox_dead_letter%'
            """;
        long count = (long)command.ExecuteScalar()!;

        count.Should().Be(1, "V003 migration script should be recorded in the journal table");
    }
}

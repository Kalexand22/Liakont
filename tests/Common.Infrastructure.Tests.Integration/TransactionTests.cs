namespace Stratum.Common.Infrastructure.Tests.Integration;

using System.Data;
using FluentAssertions;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Testing;
using Xunit;

public sealed class TransactionTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public TransactionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InsertAndSelectShouldRoundTrip()
    {
        var factory = _fixture.CreateConnectionFactory();
        using var connection = await factory.OpenAsync();

        using var createCmd = connection.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS outbox.test_round_trip (
                id SERIAL PRIMARY KEY,
                name TEXT NOT NULL
            )
            """;
        createCmd.ExecuteNonQuery();

        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO outbox.test_round_trip (name) VALUES ('stratum')";
        insertCmd.ExecuteNonQuery();

        using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT name FROM outbox.test_round_trip WHERE name = 'stratum'";
        var result = (string)selectCmd.ExecuteScalar()!;

        result.Should().Be("stratum");
    }

    [Fact]
    public async Task TransactionCommitShouldPersistData()
    {
        var factory = _fixture.CreateConnectionFactory();

        await using var scope = await TransactionScope.BeginAsync(factory);

        using var createCmd = scope.Connection.CreateCommand();
        createCmd.Transaction = scope.Transaction;
        createCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS outbox.test_commit (
                id SERIAL PRIMARY KEY,
                value TEXT NOT NULL
            )
            """;
        createCmd.ExecuteNonQuery();

        using var insertCmd = scope.Connection.CreateCommand();
        insertCmd.Transaction = scope.Transaction;
        insertCmd.CommandText = "INSERT INTO outbox.test_commit (value) VALUES ('committed')";
        insertCmd.ExecuteNonQuery();

        await scope.CommitAsync();

        // Verify data persisted outside the transaction
        using var verifyConn = await factory.OpenAsync();
        using var selectCmd = verifyConn.CreateCommand();
        selectCmd.CommandText = "SELECT value FROM outbox.test_commit WHERE value = 'committed'";
        var result = (string)selectCmd.ExecuteScalar()!;

        result.Should().Be("committed");
    }

    [Fact]
    public async Task TransactionRollbackShouldDiscardData()
    {
        var factory = _fixture.CreateConnectionFactory();

        // Setup table
        using var setupConn = await factory.OpenAsync();
        using var createCmd = setupConn.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS outbox.test_rollback (
                id SERIAL PRIMARY KEY,
                value TEXT NOT NULL
            )
            """;
        createCmd.ExecuteNonQuery();

        // Insert in transaction then rollback
        await using var scope = await TransactionScope.BeginAsync(factory);

        using var insertCmd = scope.Connection.CreateCommand();
        insertCmd.Transaction = scope.Transaction;
        insertCmd.CommandText = "INSERT INTO outbox.test_rollback (value) VALUES ('rolled_back')";
        insertCmd.ExecuteNonQuery();

        await scope.RollbackAsync();

        // Verify data was discarded
        using var verifyConn = await factory.OpenAsync();
        using var countCmd = verifyConn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM outbox.test_rollback WHERE value = 'rolled_back'";
        long count = (long)countCmd.ExecuteScalar()!;

        count.Should().Be(0);
    }
}

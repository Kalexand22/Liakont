namespace Stratum.Common.Infrastructure.Tests.Integration;

using Dapper;
using FluentAssertions;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Common.Testing;
using Xunit;

public sealed class DeadLetterQueriesTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public DeadLetterQueriesTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetPagedAsyncShouldReturnEmptyWhenTableIsEmpty()
    {
        await ClearDeadLetterTableAsync();
        var queries = new DeadLetterQueries(_fixture.CreateConnectionFactory());

        var result = await queries.GetPagedAsync(0, 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPagedAsyncShouldReturnInsertedRowsWhenTableHasEvents()
    {
        await ClearDeadLetterTableAsync();
        var id = await InsertDeadLetterEventAsync(retryCount: 5, lastError: "timeout");
        var queries = new DeadLetterQueries(_fixture.CreateConnectionFactory());

        var result = await queries.GetPagedAsync(0, 10);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(id);
        result[0].RetryCount.Should().Be(5);
        result[0].LastError.Should().Be("timeout");
    }

    [Fact]
    public async Task GetByIdAsyncShouldReturnNullWhenIdNotFound()
    {
        var queries = new DeadLetterQueries(_fixture.CreateConnectionFactory());

        var result = await queries.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsyncShouldReturnEventWhenIdExists()
    {
        await ClearDeadLetterTableAsync();
        var id = await InsertDeadLetterEventAsync(retryCount: 3, lastError: "bad request");
        var queries = new DeadLetterQueries(_fixture.CreateConnectionFactory());

        var result = await queries.GetByIdAsync(id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.RetryCount.Should().Be(3);
        result.LastError.Should().Be("bad request");
    }

    [Fact]
    public async Task GetPagedAsyncShouldRespectOffsetAndLimit()
    {
        await ClearDeadLetterTableAsync();
        for (var i = 0; i < 3; i++)
        {
            await InsertDeadLetterEventAsync(retryCount: i + 1, lastError: $"error-{i}");
        }

        var queries = new DeadLetterQueries(_fixture.CreateConnectionFactory());

        var page1 = await queries.GetPagedAsync(0, 2);
        var page2 = await queries.GetPagedAsync(2, 2);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(1);
        page1.Should().NotIntersectWith(page2, because: "pages should not overlap");
    }

    private async Task ClearDeadLetterTableAsync()
    {
        using var connection = await _fixture.CreateConnectionFactory().OpenAsync();
        await connection.ExecuteAsync("DELETE FROM outbox.dead_letter_events");
    }

    private async Task<Guid> InsertDeadLetterEventAsync(int retryCount, string? lastError)
    {
        var id = Guid.NewGuid();
        using var connection = await _fixture.CreateConnectionFactory().OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO outbox.dead_letter_events
                (id, event_type, payload, correlation_id, module_source,
                 version, occurred_at, created_at, retry_count, last_error)
            VALUES
                (@Id, 'TestEvent', '{}', gen_random_uuid(), 'Test',
                 1, now(), now(), @RetryCount, @LastError)
            """,
            new { Id = id, RetryCount = retryCount, LastError = lastError });
        return id;
    }
}

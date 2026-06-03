namespace Stratum.Common.Infrastructure.Outbox;

using Dapper;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Dapper-backed implementation of <see cref="IDeadLetterQueries"/>.
/// </summary>
public sealed class DeadLetterQueries : IDeadLetterQueries
{
    private const string SelectPagedSql = """
        SELECT id             AS "Id",
               event_type     AS "EventType",
               payload        AS "Payload",
               correlation_id AS "CorrelationId",
               module_source  AS "ModuleSource",
               version        AS "Version",
               occurred_at    AS "OccurredAt",
               created_at     AS "CreatedAt",
               retry_count    AS "RetryCount",
               last_error     AS "LastError",
               moved_at       AS "MovedAt"
        FROM outbox.dead_letter_events
        ORDER BY moved_at DESC
        OFFSET @Offset
        LIMIT @Limit
        """;

    private const string SelectByIdSql = """
        SELECT id             AS "Id",
               event_type     AS "EventType",
               payload        AS "Payload",
               correlation_id AS "CorrelationId",
               module_source  AS "ModuleSource",
               version        AS "Version",
               occurred_at    AS "OccurredAt",
               created_at     AS "CreatedAt",
               retry_count    AS "RetryCount",
               last_error     AS "LastError",
               moved_at       AS "MovedAt"
        FROM outbox.dead_letter_events
        WHERE id = @Id
        """;

    private readonly ISystemConnectionFactory _connectionFactory;

    public DeadLetterQueries(ISystemConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DeadLetterEvent>> GetPagedAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<DeadLetterEvent>(
            new CommandDefinition(SelectPagedSql, new { Offset = offset, Limit = limit }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<DeadLetterEvent?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<DeadLetterEvent>(
            new CommandDefinition(SelectByIdSql, new { Id = id }, cancellationToken: cancellationToken));
    }
}

namespace Stratum.Modules.Job.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Job.Contracts.DTOs;
using Stratum.Modules.Job.Contracts.Queries;

internal sealed class PostgresJobQueries : IJobQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresJobQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<JobDto?> GetByIdAsync(Guid jobId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, type, status, priority, max_retries, retry_count,
                   scheduled_at, started_at, completed_at, error_message, company_id, created_at
            FROM job.jobs
            WHERE id = @Id
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = jobId }, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return new JobDto
        {
            Id = (Guid)row.id,
            Type = (string)row.type,
            Status = (string)row.status,
            Priority = (int)row.priority,
            MaxRetries = (int)row.max_retries,
            RetryCount = (int)row.retry_count,
            ScheduledAt = (DateTimeOffset)row.scheduled_at,
            StartedAt = (DateTimeOffset?)row.started_at,
            CompletedAt = (DateTimeOffset?)row.completed_at,
            ErrorMessage = (string?)row.error_message,
            CompanyId = (Guid?)row.company_id,
            CreatedAt = (DateTimeOffset)row.created_at,
        };
    }

    public async Task<IReadOnlyList<JobDto>> ListByStatusAsync(string status, int limit = 50, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, type, status, priority, max_retries, retry_count,
                   scheduled_at, started_at, completed_at, error_message, company_id, created_at
            FROM job.jobs
            WHERE status = @Status
            ORDER BY created_at DESC
            LIMIT @Limit
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { Status = status, Limit = limit }, cancellationToken: ct));

        return rows.Select(row => new JobDto
        {
            Id = (Guid)row.id,
            Type = (string)row.type,
            Status = (string)row.status,
            Priority = (int)row.priority,
            MaxRetries = (int)row.max_retries,
            RetryCount = (int)row.retry_count,
            ScheduledAt = (DateTimeOffset)row.scheduled_at,
            StartedAt = (DateTimeOffset?)row.started_at,
            CompletedAt = (DateTimeOffset?)row.completed_at,
            ErrorMessage = (string?)row.error_message,
            CompanyId = (Guid?)row.company_id,
            CreatedAt = (DateTimeOffset)row.created_at,
        }).ToList();
    }
}

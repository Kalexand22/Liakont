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
            ScheduledAt = DbTimestamp.ToDateTimeOffset((object)row.scheduled_at),
            StartedAt = DbTimestamp.ToNullableDateTimeOffset((object?)row.started_at),
            CompletedAt = DbTimestamp.ToNullableDateTimeOffset((object?)row.completed_at),
            ErrorMessage = (string?)row.error_message,
            CompanyId = (Guid?)row.company_id,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)row.created_at),
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
            ScheduledAt = DbTimestamp.ToDateTimeOffset((object)row.scheduled_at),
            StartedAt = DbTimestamp.ToNullableDateTimeOffset((object?)row.started_at),
            CompletedAt = DbTimestamp.ToNullableDateTimeOffset((object?)row.completed_at),
            ErrorMessage = (string?)row.error_message,
            CompanyId = (Guid?)row.company_id,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)row.created_at),
        }).ToList();
    }

    // Liakont addition (FIX210) : dernier achèvement d'un type de job, filtré en SQL (pas de scan plafonné).
    public async Task<DateTimeOffset?> GetLastCompletedAtByTypeAsync(string jobType, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT max(completed_at)
            FROM job.jobs
            WHERE type = @Type AND status = 'Completed'
            """;

        var lastCompleted = await conn.ExecuteScalarAsync<object?>(
            new CommandDefinition(sql, new { Type = jobType }, cancellationToken: ct));
        return DbTimestamp.ToNullableDateTimeOffset(lastCompleted);
    }

    // Liakont addition (RDL08) : dé-duplication à l'enqueue (A6-scale-2). Existence d'un job 'Pending' du même
    // type ET de la même portée tenant (company_id NULL pour les jobs système — 'IS NOT DISTINCT FROM' gère
    // l'égalité NULL). Limité à 'Pending' (pas 'Running') pour ne jamais bloquer sur un Running orphelin
    // (A6-scale-1, aucun reaper). Voir docs/adr/ADR-0006 §5.
    public async Task<bool> HasPendingJobOfTypeAsync(string jobType, Guid? companyId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM job.jobs
                WHERE type = @Type
                  AND status = 'Pending'
                  AND company_id IS NOT DISTINCT FROM @CompanyId
            )
            """;

        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { Type = jobType, CompanyId = companyId }, cancellationToken: ct));
    }
}

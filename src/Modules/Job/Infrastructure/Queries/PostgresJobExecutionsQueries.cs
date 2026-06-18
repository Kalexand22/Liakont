namespace Stratum.Modules.Job.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Job.Contracts.DTOs;
using Stratum.Modules.Job.Contracts.Queries;

// Liakont addition (FIX211) : read-model d'administration des exécutions de jobs (job.jobs), tenant-scopé.
internal sealed class PostgresJobExecutionsQueries : IJobExecutionsQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresJobExecutionsQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<JobDto>> ListAsync(JobExecutionsFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        // Tenant-scopé par company_id (CLAUDE.md n°9) ; les filtres statut/type/période sont optionnels
        // (un paramètre NULL n'applique aucune contrainte). Tri par création décroissante, plafonné.
        const string sql = """
            SELECT id, type, status, priority, max_retries, retry_count,
                   scheduled_at, started_at, completed_at, error_message, company_id, created_at
            FROM job.jobs
            WHERE company_id = @CompanyId
              AND (@Status IS NULL OR status = @Status)
              AND (@Type IS NULL OR type = @Type)
              AND (@From IS NULL OR created_at >= @From)
              AND (@To IS NULL OR created_at <= @To)
            ORDER BY created_at DESC
            LIMIT @Limit
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(
                sql,
                new
                {
                    filter.CompanyId,
                    Status = string.IsNullOrWhiteSpace(filter.Status) ? null : filter.Status,
                    Type = string.IsNullOrWhiteSpace(filter.Type) ? null : filter.Type,
                    filter.From,
                    filter.To,
                    Limit = filter.Limit <= 0 ? 200 : filter.Limit,
                },
                cancellationToken: ct));

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
}

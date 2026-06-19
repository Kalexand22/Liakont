namespace Stratum.Modules.Job.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Job.Contracts.DTOs;
using Stratum.Modules.Job.Contracts.Queries;

internal sealed class PostgresScheduleQueries : IScheduleQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresScheduleQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ScheduleDto?> GetByIdAsync(Guid scheduleId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, name, cron_expression, job_type, payload_template,
                   is_active, next_run_at, last_run_at, company_id, created_at, updated_at
            FROM job.schedules
            WHERE id = @Id
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = scheduleId }, cancellationToken: ct));

        return MapRow(row);
    }

    public async Task<IReadOnlyList<ScheduleDto>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, name, cron_expression, job_type, payload_template,
                   is_active, next_run_at, last_run_at, company_id, created_at, updated_at
            FROM job.schedules
            WHERE company_id = @CompanyId
            ORDER BY name
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        var result = new List<ScheduleDto>();
        foreach (var row in rows)
        {
            var dto = MapRow(row);
            if (dto is not null)
            {
                result.Add(dto);
            }
        }

        return result;
    }

    private static ScheduleDto? MapRow(dynamic? row)
    {
        if (row is null)
        {
            return null;
        }

        return new ScheduleDto
        {
            Id = (Guid)row.id,
            Name = (string)row.name,
            CronExpression = (string)row.cron_expression,
            JobType = (string)row.job_type,
            PayloadTemplate = (string)row.payload_template,
            IsActive = (bool)row.is_active,
            NextRunAt = DbTimestamp.ToDateTimeOffset((object)row.next_run_at),
            LastRunAt = DbTimestamp.ToNullableDateTimeOffset((object?)row.last_run_at),
            CompanyId = (Guid)row.company_id,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)row.created_at),
            UpdatedAt = DbTimestamp.ToDateTimeOffset((object)row.updated_at),
        };
    }
}

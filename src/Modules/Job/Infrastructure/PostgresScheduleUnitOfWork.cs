namespace Stratum.Modules.Job.Infrastructure;

using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Domain.Entities;

internal sealed class PostgresScheduleUnitOfWork : IScheduleUnitOfWork
{
    private readonly TransactionScope _txn;

    private PostgresScheduleUnitOfWork(TransactionScope txn)
    {
        _txn = txn;
    }

    public static async Task<PostgresScheduleUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken ct = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, ct);
        return new PostgresScheduleUnitOfWork(txn);
    }

    public async Task InsertScheduleAsync(JobSchedule schedule, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO job.schedules
                (id, name, cron_expression, job_type, payload_template, is_active,
                 next_run_at, last_run_at, company_id, created_at, updated_at)
            VALUES
                (@Id, @Name, @CronExpression, @JobType, @PayloadTemplate::jsonb, @IsActive,
                 @NextRunAt, @LastRunAt, @CompanyId, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                schedule.Id,
                schedule.Name,
                schedule.CronExpression,
                schedule.JobType,
                schedule.PayloadTemplate,
                schedule.IsActive,
                schedule.NextRunAt,
                schedule.LastRunAt,
                schedule.CompanyId,
                schedule.CreatedAt,
                schedule.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateScheduleAsync(JobSchedule schedule, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE job.schedules
            SET name = @Name,
                cron_expression = @CronExpression,
                job_type = @JobType,
                payload_template = @PayloadTemplate::jsonb,
                is_active = @IsActive,
                next_run_at = @NextRunAt,
                last_run_at = @LastRunAt,
                updated_at = @UpdatedAt
            WHERE id = @Id
            """;

        var rowsAffected = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                schedule.Id,
                schedule.Name,
                schedule.CronExpression,
                schedule.JobType,
                schedule.PayloadTemplate,
                schedule.IsActive,
                schedule.NextRunAt,
                schedule.LastRunAt,
                schedule.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));

        if (rowsAffected != 1)
        {
            throw new InvalidOperationException($"Schedule {schedule.Id} was not found.");
        }
    }

    public async Task<JobSchedule?> GetScheduleByIdAsync(Guid scheduleId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, name, cron_expression, job_type, payload_template,
                   is_active, next_run_at, last_run_at, company_id, created_at, updated_at
            FROM job.schedules
            WHERE id = @Id
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = scheduleId }, _txn.Transaction, cancellationToken: ct));

        return ReconstructFromRow(row);
    }

    public async Task<bool> ExistsByNameAndCompanyAsync(
        string name,
        Guid companyId,
        Guid? excludeId = null,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM job.schedules
                WHERE name = @Name AND company_id = @CompanyId
                  AND (@ExcludeId IS NULL OR id <> @ExcludeId)
            )
            """;

        return await _txn.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql,
            new { Name = name, CompanyId = companyId, ExcludeId = excludeId },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<JobSchedule>> GetDueSchedulesAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, name, cron_expression, job_type, payload_template,
                   is_active, next_run_at, last_run_at, company_id, created_at, updated_at
            FROM job.schedules
            WHERE is_active = true AND next_run_at <= @Now
            ORDER BY next_run_at
            FOR UPDATE SKIP LOCKED
            """;

        var rows = await _txn.Connection.QueryAsync(
            new CommandDefinition(sql, new { Now = now }, _txn.Transaction, cancellationToken: ct));

        var schedules = new List<JobSchedule>();
        foreach (var row in rows)
        {
            var schedule = ReconstructFromRow(row);
            if (schedule is not null)
            {
                schedules.Add(schedule);
            }
        }

        return schedules;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _txn.CommitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }

    private static JobSchedule? ReconstructFromRow(dynamic? row)
    {
        if (row is null)
        {
            return null;
        }

        return JobSchedule.Reconstitute(
            (Guid)row.id,
            (string)row.name,
            (string)row.cron_expression,
            (string)row.job_type,
            (string)row.payload_template,
            (bool)row.is_active,
            (DateTimeOffset)row.next_run_at,
            (DateTimeOffset?)row.last_run_at,
            (Guid)row.company_id,
            (DateTimeOffset)row.created_at,
            (DateTimeOffset)row.updated_at);
    }
}

namespace Stratum.Modules.Job.Infrastructure;

using Dapper;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Domain.Entities;

internal sealed class PostgresJobUnitOfWork : IJobUnitOfWork
{
    private readonly IOutboxWriter _outboxWriter;
    private readonly TransactionScope _txn;

    private PostgresJobUnitOfWork(TransactionScope txn, IOutboxWriter outboxWriter)
    {
        _outboxWriter = outboxWriter;
        _txn = txn;
    }

    public static async Task<PostgresJobUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        IOutboxWriter outboxWriter,
        CancellationToken ct = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, ct);
        return new PostgresJobUnitOfWork(txn, outboxWriter);
    }

    public async Task InsertJobAsync(JobEntry job, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO job.jobs (id, type, payload, status, priority, max_retries, retry_count, scheduled_at, started_at, completed_at, error_message, company_id, created_at)
            VALUES (@Id, @Type, @Payload::jsonb, @Status, @Priority, @MaxRetries, @RetryCount, @ScheduledAt, @StartedAt, @CompletedAt, @ErrorMessage, @CompanyId, @CreatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                job.Id,
                job.Type,
                job.Payload,
                Status = job.Status.Value,
                job.Priority,
                job.MaxRetries,
                job.RetryCount,
                job.ScheduledAt,
                job.StartedAt,
                job.CompletedAt,
                job.ErrorMessage,
                job.CompanyId,
                job.CreatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateJobAsync(JobEntry job, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE job.jobs
            SET status = @Status,
                retry_count = @RetryCount,
                started_at = @StartedAt,
                completed_at = @CompletedAt,
                error_message = @ErrorMessage
            WHERE id = @Id
            """;

        var rowsAffected = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                job.Id,
                Status = job.Status.Value,
                job.RetryCount,
                job.StartedAt,
                job.CompletedAt,
                job.ErrorMessage,
            },
            _txn.Transaction,
            cancellationToken: ct));

        if (rowsAffected != 1)
        {
            throw new InvalidOperationException($"Job {job.Id} was not found.");
        }
    }

    public async Task<JobEntry?> GetJobByIdAsync(Guid jobId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, type, payload, status, priority, max_retries, retry_count,
                   scheduled_at, started_at, completed_at, error_message, company_id, created_at
            FROM job.jobs
            WHERE id = @Id
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = jobId }, _txn.Transaction, cancellationToken: ct));

        return ReconstructFromRow(row);
    }

    public async Task<JobEntry?> AcquireNextPendingJobAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, type, payload, status, priority, max_retries, retry_count,
                   scheduled_at, started_at, completed_at, error_message, company_id, created_at
            FROM job.jobs
            WHERE status = 'Pending'
              AND scheduled_at <= now()
            ORDER BY priority DESC, created_at
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, transaction: _txn.Transaction, cancellationToken: ct));

        return ReconstructFromRow(row);
    }

    public async Task CommitWithEventAsync<TPayload>(
        IntegrationEvent<TPayload> integrationEvent,
        CancellationToken ct = default)
    {
        await _outboxWriter.WriteAsync(_txn, integrationEvent, ct);
        await _txn.CommitAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _txn.CommitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }

    private static JobEntry? ReconstructFromRow(dynamic? row)
    {
        if (row is null)
        {
            return null;
        }

        return JobEntry.Reconstitute(
            (Guid)row.id,
            (string)row.type,
            (string)row.payload,
            (string)row.status,
            (int)row.priority,
            (int)row.max_retries,
            (int)row.retry_count,
            DbTimestamp.ToDateTimeOffset((object)row.scheduled_at),
            DbTimestamp.ToNullableDateTimeOffset((object?)row.started_at),
            DbTimestamp.ToNullableDateTimeOffset((object?)row.completed_at),
            (string?)row.error_message,
            (Guid?)row.company_id,
            DbTimestamp.ToDateTimeOffset((object)row.created_at));
    }
}

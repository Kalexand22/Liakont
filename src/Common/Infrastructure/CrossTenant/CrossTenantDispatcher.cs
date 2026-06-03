namespace Stratum.Common.Infrastructure.CrossTenant;

using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.BlobStorage;
using Stratum.Common.Abstractions.CrossTenant;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Background service that polls <c>outbox.cross_tenant_events</c> for pending events,
/// resolves handlers via <see cref="ICrossTenantHandlerRegistry"/>, and delivers them
/// to target tenants. Failed deliveries are retried up to <see cref="CrossTenantDispatcherOptions.MaxRetries"/>
/// times before being marked as dead-letter.
/// </summary>
/// <remarks>
/// Concurrency safety relies on idempotent status transitions:
/// <c>MarkDeliveredSql</c> uses <c>WHERE status IN ('pending','failed')</c>,
/// so duplicate processing by multiple instances is harmless.
/// Handlers must be idempotent (at-least-once delivery guarantee).
/// </remarks>
public sealed partial class CrossTenantDispatcher : BackgroundService
{
    private const int MaxConcurrentTenants = 8;

    private const string SelectPendingSql = """
        SELECT id               AS "Id",
               source_tenant    AS "SourceTenant",
               target_tenant    AS "TargetTenant",
               event_type       AS "EventType",
               payload          AS "Payload",
               blob_refs        AS "BlobRefs",
               submitter_email  AS "SubmitterEmail",
               created_at       AS "CreatedAt",
               retry_count      AS "RetryCount"
        FROM outbox.cross_tenant_events
        WHERE status IN ('pending', 'failed')
          AND retry_count < @MaxRetries
        ORDER BY created_at
        LIMIT @BatchSize
        """;

    private const string MarkDeliveredSql = """
        UPDATE outbox.cross_tenant_events
        SET status = 'delivered', delivered_at = NOW()
        WHERE id = @EventId AND status IN ('pending', 'failed')
        """;

    private const string IncrementRetrySql = """
        UPDATE outbox.cross_tenant_events
        SET retry_count = retry_count + 1,
            last_error  = @LastError,
            status      = 'failed'
        WHERE id = @EventId
        """;

    private const string MarkDeadSql = """
        UPDATE outbox.cross_tenant_events
        SET retry_count = retry_count + 1,
            last_error  = @LastError,
            status      = 'dead'
        WHERE id = @EventId
        """;

    private readonly ISystemConnectionFactory _systemConnectionFactory;
    private readonly ICrossTenantHandlerRegistry _handlerRegistry;
    private readonly CrossTenantDispatcherOptions _options;
    private readonly ILogger<CrossTenantDispatcher> _logger;

    public CrossTenantDispatcher(
        ISystemConnectionFactory systemConnectionFactory,
        ICrossTenantHandlerRegistry handlerRegistry,
        IOptions<CrossTenantDispatcherOptions> options,
        ILogger<CrossTenantDispatcher> logger)
    {
        _systemConnectionFactory = systemConnectionFactory;
        _handlerRegistry = handlerRegistry;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessBatchAsync(stoppingToken);

                if (processedCount == 0)
                {
                    await Task.Delay(_options.PollingInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogBatchError(_logger, ex);
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
        }

        LogStopped(_logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "CrossTenantDispatcher started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "CrossTenantDispatcher stopped")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing cross-tenant dispatch batch")]
    private static partial void LogBatchError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing cross-tenant batch: {Count} event(s)")]
    private static partial void LogBatchStart(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cross-tenant batch complete: {Count} event(s) delivered")]
    private static partial void LogBatchComplete(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No handler registered for cross-tenant event type '{EventType}' ({EventId}), marking as delivered")]
    private static partial void LogNoHandler(ILogger logger, string eventType, Guid eventId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cross-tenant event delivered: {EventType} ({EventId}) to={TargetTenant}")]
    private static partial void LogDelivered(ILogger logger, string eventType, Guid eventId, string targetTenant);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cross-tenant event delivery failed: {EventType} ({EventId}) to={TargetTenant}, will retry")]
    private static partial void LogDeliveryFailed(ILogger logger, string eventType, Guid eventId, string targetTenant, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cross-tenant event exhausted retries: {EventType} ({EventId}), moved to dead-letter after {RetryCount} attempts")]
    private static partial void LogDead(ILogger logger, string eventType, Guid eventId, int retryCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to update status for cross-tenant event {EventId}")]
    private static partial void LogStatusUpdateError(ILogger logger, Guid eventId, Exception exception);

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        List<CrossTenantEventRow> rows;

        using (var connection = await _systemConnectionFactory.OpenAsync(ct))
        {
            rows = (await connection.QueryAsync<CrossTenantEventRow>(
                new CommandDefinition(
                    SelectPendingSql,
                    new { _options.MaxRetries, _options.BatchSize },
                    cancellationToken: ct)))
                .ToList();
        }

        if (rows.Count == 0)
        {
            return 0;
        }

        LogBatchStart(_logger, rows.Count);

        var groups = rows.GroupBy(r => r.TargetTenant).ToList();
        var totalProcessed = 0;

        await Parallel.ForEachAsync(
            groups,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentTenants, CancellationToken = ct },
            async (group, innerCt) =>
            {
                foreach (var row in group)
                {
                    if (await TryDeliverEventAsync(group.Key, row, innerCt))
                    {
                        Interlocked.Increment(ref totalProcessed);
                    }
                }
            });

        LogBatchComplete(_logger, totalProcessed);
        return totalProcessed;
    }

    private async Task<bool> TryDeliverEventAsync(
        string targetTenant,
        CrossTenantEventRow row,
        CancellationToken ct)
    {
        var handler = _handlerRegistry.Resolve(row.EventType);

        if (handler is null)
        {
            LogNoHandler(_logger, row.EventType, row.Id);
            await UpdateStatusAsync(MarkDeliveredSql, row.Id, null, ct);
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(row.Payload);
            var payloadElement = doc.RootElement.Clone();
            IReadOnlyList<BlobReference>? blobs = row.BlobRefs is not null
                ? JsonSerializer.Deserialize<List<BlobReference>>(row.BlobRefs, CrossTenantJsonOptions.Instance)
                : null;

            var envelope = new CrossTenantEnvelope(
                Id: row.Id,
                SourceTenant: row.SourceTenant,
                TargetTenant: row.TargetTenant,
                EventType: row.EventType,
                Payload: payloadElement,
                Blobs: blobs,
                SubmitterEmail: row.SubmitterEmail,
                CreatedAt: row.CreatedAt);

            await handler.HandleAsync(envelope, envelope.Payload, ct);

            await UpdateStatusAsync(MarkDeliveredSql, row.Id, null, ct);
            LogDelivered(_logger, row.EventType, row.Id, targetTenant);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var newRetryCount = row.RetryCount + 1;

            if (newRetryCount >= _options.MaxRetries)
            {
                await UpdateStatusAsync(MarkDeadSql, row.Id, ex.Message, ct);
                LogDead(_logger, row.EventType, row.Id, newRetryCount);
            }
            else
            {
                await UpdateStatusAsync(IncrementRetrySql, row.Id, ex.Message, ct);
                LogDeliveryFailed(_logger, row.EventType, row.Id, targetTenant, ex);
            }

            return false;
        }
    }

    private async Task UpdateStatusAsync(string sql, Guid eventId, string? lastError, CancellationToken ct)
    {
        try
        {
            using var connection = await _systemConnectionFactory.OpenAsync(ct);
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { EventId = eventId, LastError = lastError }, cancellationToken: ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStatusUpdateError(_logger, eventId, ex);
        }
    }

    /// <summary>
    /// Row model for events read from <c>outbox.cross_tenant_events</c>.
    /// </summary>
    private sealed record CrossTenantEventRow
    {
        public Guid Id { get; init; }

        public string? SourceTenant { get; init; }

        public string TargetTenant { get; init; } = string.Empty;

        public string EventType { get; init; } = string.Empty;

        public string Payload { get; init; } = string.Empty;

        public string? BlobRefs { get; init; }

        public string? SubmitterEmail { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public int RetryCount { get; init; }
    }
}

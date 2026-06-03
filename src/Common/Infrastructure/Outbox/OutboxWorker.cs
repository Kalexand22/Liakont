namespace Stratum.Common.Infrastructure.Outbox;

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Background worker that polls the outbox table for pending events,
/// dispatches them via IEventDispatcher, and marks them as processed.
/// Duplicate EventIds are handled idempotently.
/// </summary>
public sealed partial class OutboxWorker : BackgroundService
{
    private const string SelectPendingSql = """
        SELECT id             AS "Id",
               event_type     AS "EventType",
               payload        AS "Payload",
               correlation_id AS "CorrelationId",
               module_source  AS "ModuleSource",
               version        AS "Version",
               occurred_at    AS "OccurredAt",
               retry_count    AS "RetryCount"
        FROM outbox.pending_events
        WHERE processed_at IS NULL
        ORDER BY created_at
        LIMIT @BatchSize
        """;

    private const string MarkProcessedSql = """
        UPDATE outbox.pending_events
        SET processed_at = NOW()
        WHERE id = @EventId AND processed_at IS NULL
        """;

    private const string IncrementRetryCountSql = """
        UPDATE outbox.pending_events
        SET retry_count = retry_count + 1,
            last_error  = @LastError
        WHERE id = @EventId
        """;

    private const string MoveToDeadLetterSql = """
        WITH deleted AS (
            DELETE FROM outbox.pending_events
            WHERE id = @EventId
            RETURNING id, event_type, payload, correlation_id, module_source,
                      version, occurred_at, created_at
        )
        INSERT INTO outbox.dead_letter_events
            (id, event_type, payload, correlation_id, module_source,
             version, occurred_at, created_at, retry_count, last_error)
        SELECT id, event_type, payload, correlation_id, module_source,
               version, occurred_at, created_at, @RetryCount, @LastError
        FROM deleted
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly ConcurrentDictionary<Type, MethodInfo> DispatchMethodCache = new();

    private readonly ISystemConnectionFactory _connectionFactory;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly IEventTypeRegistry _eventTypeRegistry;
    private readonly OutboxWorkerOptions _options;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(
        ISystemConnectionFactory connectionFactory,
        IEventDispatcher eventDispatcher,
        IEventTypeRegistry eventTypeRegistry,
        IOptions<OutboxWorkerOptions> options,
        ILogger<OutboxWorker> logger)
    {
        _connectionFactory = connectionFactory;
        _eventDispatcher = eventDispatcher;
        _eventTypeRegistry = eventTypeRegistry;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogWorkerStarted(_logger);

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
                LogProcessingError(_logger, ex);
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
        }

        LogWorkerStopped(_logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox worker started")]
    private static partial void LogWorkerStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox worker stopped")]
    private static partial void LogWorkerStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing outbox batch")]
    private static partial void LogProcessingError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown event type '{EventType}' for event {EventId}, marking as processed")]
    private static partial void LogUnknownEventType(ILogger logger, string eventType, Guid eventId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to dispatch event {EventType} ({EventId}) correlation={CorrelationId}, will retry next cycle")]
    private static partial void LogDispatchFailed(ILogger logger, string eventType, Guid eventId, Guid correlationId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Event {EventType} ({EventId}) exhausted {RetryCount} retries, moving to dead-letter")]
    private static partial void LogMovedToDeadLetter(ILogger logger, string eventType, Guid eventId, int retryCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to record dispatch failure for event {EventType} ({EventId}); batch continues")]
    private static partial void LogFailureTrackingError(ILogger logger, string eventType, Guid eventId, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing outbox batch, up to {BatchSize} events")]
    private static partial void LogBatchStart(ILogger logger, int batchSize);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outbox batch complete, processed {ProcessedCount} events")]
    private static partial void LogBatchComplete(ILogger logger, int processedCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Event {EventType} ({EventId}) already processed, skipping")]
    private static partial void LogAlreadyProcessed(ILogger logger, string eventType, Guid eventId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outbox event processed: {EventType} ({EventId}) correlation={CorrelationId}")]
    private static partial void LogEventProcessed(ILogger logger, string eventType, Guid eventId, Guid correlationId);

    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        LogBatchStart(_logger, _options.BatchSize);

        using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<OutboxEventRow>(
            new CommandDefinition(SelectPendingSql, new { _options.BatchSize }, cancellationToken: cancellationToken));

        var processedCount = 0;

        foreach (var row in rows)
        {
            if (await TryProcessEventAsync(connection, row, cancellationToken))
            {
                processedCount++;
            }
        }

        LogBatchComplete(_logger, processedCount);
        return processedCount;
    }

    private async Task<bool> TryProcessEventAsync(
        System.Data.IDbConnection connection,
        OutboxEventRow row,
        CancellationToken cancellationToken)
    {
        var payloadType = _eventTypeRegistry.GetPayloadType(row.EventType);

        if (payloadType is null)
        {
            LogUnknownEventType(_logger, row.EventType, row.Id);

            // Mark as processed to avoid infinite retry of unrecognized events
            await connection.ExecuteAsync(
                new CommandDefinition(MarkProcessedSql, new { EventId = row.Id }, cancellationToken: cancellationToken));

            return true;
        }

        try
        {
            await DispatchEventAsync(row, payloadType, cancellationToken);
        }
        catch (Exception ex)
        {
            LogDispatchFailed(_logger, row.EventType, row.Id, row.CorrelationId, ex);
            await HandleDispatchFailureAsync(connection, row, ex.Message, cancellationToken);
            return false;
        }

        // Idempotency guard: only mark processed if not already claimed by another process
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(MarkProcessedSql, new { EventId = row.Id }, cancellationToken: cancellationToken));

        if (affected == 0)
        {
            LogAlreadyProcessed(_logger, row.EventType, row.Id);
            return false;
        }

        LogEventProcessed(_logger, row.EventType, row.Id, row.CorrelationId);
        return true;
    }

    private async Task HandleDispatchFailureAsync(
        System.Data.IDbConnection connection,
        OutboxEventRow row,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var newRetryCount = row.RetryCount + 1;

        try
        {
            if (newRetryCount >= _options.MaxRetries)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    MoveToDeadLetterSql,
                    new { EventId = row.Id, RetryCount = newRetryCount, LastError = errorMessage },
                    cancellationToken: cancellationToken));

                LogMovedToDeadLetter(_logger, row.EventType, row.Id, newRetryCount);
            }
            else
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    IncrementRetryCountSql,
                    new { EventId = row.Id, LastError = errorMessage },
                    cancellationToken: cancellationToken));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailureTrackingError(_logger, row.EventType, row.Id, ex);
        }
    }

    private async Task DispatchEventAsync(OutboxEventRow row, Type payloadType, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize(row.Payload, payloadType, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize payload for event {row.Id} (type: {row.EventType}).");

        // Build IntegrationEvent<TPayload> via reflection
        var eventType = typeof(IntegrationEvent<>).MakeGenericType(payloadType);
        var integrationEvent = Activator.CreateInstance(eventType, nonPublic: true)
            ?? throw new InvalidOperationException($"Failed to create IntegrationEvent<{payloadType.Name}>.");

        eventType.GetProperty(nameof(IntegrationEvent<object>.EventId))!.SetValue(integrationEvent, row.Id);
        eventType.GetProperty(nameof(IntegrationEvent<object>.EventType))!.SetValue(integrationEvent, row.EventType);
        eventType.GetProperty(nameof(IntegrationEvent<object>.OccurredAt))!.SetValue(integrationEvent, row.OccurredAt);
        eventType.GetProperty(nameof(IntegrationEvent<object>.CorrelationId))!.SetValue(integrationEvent, row.CorrelationId);
        eventType.GetProperty(nameof(IntegrationEvent<object>.ModuleSource))!.SetValue(integrationEvent, row.ModuleSource);
        eventType.GetProperty(nameof(IntegrationEvent<object>.Payload))!.SetValue(integrationEvent, payload);
        eventType.GetProperty(nameof(IntegrationEvent<object>.Version))!.SetValue(integrationEvent, row.Version);

        // Call IEventDispatcher.PublishAsync<TPayload> via cached reflection
        var publishMethod = DispatchMethodCache.GetOrAdd(payloadType, static type =>
        {
            return typeof(IEventDispatcher)
                .GetMethod(nameof(IEventDispatcher.PublishAsync))!
                .MakeGenericMethod(type);
        });

        var task = (Task)publishMethod.Invoke(_eventDispatcher, [integrationEvent, cancellationToken])!;
        await task;
    }

    /// <summary>
    /// Represents a row from the outbox.pending_events table.
    /// </summary>
    private sealed record OutboxEventRow
    {
        public Guid Id { get; init; }

        public string EventType { get; init; } = string.Empty;

        public string Payload { get; init; } = string.Empty;

        public Guid CorrelationId { get; init; }

        public string ModuleSource { get; init; } = string.Empty;

        public int Version { get; init; }

        public DateTimeOffset OccurredAt { get; init; }

        public int RetryCount { get; init; }
    }
}

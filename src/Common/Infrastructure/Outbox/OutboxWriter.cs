namespace Stratum.Common.Infrastructure.Outbox;

using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Inserts integration events into outbox.pending_events within an existing database transaction.
/// </summary>
public sealed partial class OutboxWriter : IOutboxWriter
{
    private const string InsertSql = """
        INSERT INTO outbox.pending_events (id, event_type, payload, correlation_id, module_source, version, occurred_at)
        VALUES (@Id, @EventType, @Payload::jsonb, @CorrelationId, @ModuleSource, @Version, @OccurredAt)
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<OutboxWriter> _logger;

    public OutboxWriter(ILogger<OutboxWriter> logger)
    {
        _logger = logger;
    }

    public async Task WriteAsync<TPayload>(
        ITransactionScope transaction,
        IntegrationEvent<TPayload> integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(integrationEvent.Payload, JsonOptions);

        var parameters = new
        {
            Id = integrationEvent.EventId,
            integrationEvent.EventType,
            Payload = payload,
            integrationEvent.CorrelationId,
            integrationEvent.ModuleSource,
            integrationEvent.Version,
            integrationEvent.OccurredAt,
        };

        await transaction.Connection.ExecuteAsync(
            new CommandDefinition(InsertSql, parameters, transaction.Transaction, cancellationToken: cancellationToken));

        LogEventWritten(_logger, integrationEvent.EventType, integrationEvent.EventId, integrationEvent.CorrelationId);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outbox event written: {EventType} ({EventId}) correlation={CorrelationId}")]
    private static partial void LogEventWritten(ILogger logger, string eventType, Guid eventId, Guid correlationId);
}

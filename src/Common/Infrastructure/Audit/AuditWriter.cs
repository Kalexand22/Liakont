namespace Stratum.Common.Infrastructure.Audit;

using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Dapper-backed implementation of <see cref="IAuditWriter"/> that persists field-level
/// changes to <c>audit.field_changes</c>.
/// </summary>
/// <remarks>
/// Write failures are swallowed and logged at Warning level in compliance with
/// INV-AUDIT-002: audit writes must never fail the business transaction.
/// </remarks>
public sealed partial class AuditWriter : IAuditWriter
{
    private const string InsertSql = """
        INSERT INTO audit.field_changes (entry_id, entity_type, entity_id, field_name, old_value, new_value, actor_id)
        VALUES (@EntryId, @EntityType, @EntityId, @FieldName, @OldValue::jsonb, @NewValue::jsonb, @ActorId)
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ISystemConnectionFactory _connectionFactory;
    private readonly ILogger<AuditWriter> _logger;

    public AuditWriter(ISystemConnectionFactory connectionFactory, ILogger<AuditWriter> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task WriteChangeAsync(
        Guid entryId,
        string entityType,
        string entityId,
        string fieldName,
        object? oldValue,
        object? newValue,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = new
            {
                EntryId = entryId,
                EntityType = entityType,
                EntityId = entityId,
                FieldName = fieldName,
                OldValue = Serialize(oldValue),
                NewValue = Serialize(newValue),
                ActorId = actorId,
            };

            using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            await connection.ExecuteAsync(
                new CommandDefinition(InsertSql, parameters, cancellationToken: cancellationToken));

            LogChangeWritten(_logger, entityType, entityId, fieldName, entryId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWriteFailed(_logger, ex, entityType, entityId, fieldName);
        }
    }

    private static string? Serialize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string s)
        {
            // Wrap raw strings as JSON string values so the column stays valid jsonb
            return JsonSerializer.Serialize(s, JsonOptions);
        }

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Audit field change written: {EntityType}/{EntityId}.{FieldName} (entry={EntryId})")]
    private static partial void LogChangeWritten(
        ILogger logger, string entityType, string entityId, string fieldName, Guid entryId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Audit write failed for {EntityType}/{EntityId}.{FieldName} — audit does not fail the business transaction")]
    private static partial void LogWriteFailed(
        ILogger logger, Exception ex, string entityType, string entityId, string fieldName);
}

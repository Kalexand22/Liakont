namespace Stratum.Common.Infrastructure.Audit;

using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Dapper-backed implementation of <see cref="IActivityLogger"/> that persists business-level
/// activities to <c>audit.activities</c>.
/// </summary>
/// <remarks>
/// Write failures are swallowed and logged at Warning level in compliance with
/// INV-AUDIT-002: audit writes must never fail the business transaction.
/// </remarks>
public sealed partial class ActivityLogger : IActivityLogger
{
    private const string InsertSql = """
        INSERT INTO audit.activities (entity_type, entity_id, activity_type, description, actor_id, metadata, company_id)
        VALUES (@EntityType, @EntityId, @ActivityType, @Description, @ActorId, @Metadata::jsonb, @CompanyId)
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ISystemConnectionFactory _connectionFactory;
    private readonly ILogger<ActivityLogger> _logger;

    public ActivityLogger(ISystemConnectionFactory connectionFactory, ILogger<ActivityLogger> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task LogActivityAsync(
        string entityType,
        string entityId,
        string activityType,
        string description,
        string actorId,
        object? metadata = null,
        Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = new
            {
                EntityType = entityType,
                EntityId = entityId,
                ActivityType = activityType,
                Description = description,
                ActorId = actorId,
                Metadata = SerializeMetadata(metadata),
                CompanyId = companyId,
            };

            using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            await connection.ExecuteAsync(
                new CommandDefinition(InsertSql, parameters, cancellationToken: cancellationToken));

            LogActivityWritten(_logger, entityType, entityId, activityType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWriteFailed(_logger, ex, entityType, entityId, activityType);
        }
    }

    private static string? SerializeMetadata(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Activity logged: {EntityType}/{EntityId} [{ActivityType}]")]
    private static partial void LogActivityWritten(
        ILogger logger, string entityType, string entityId, string activityType);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Activity write failed for {EntityType}/{EntityId} [{ActivityType}] — activity does not fail the business transaction")]
    private static partial void LogWriteFailed(
        ILogger logger, Exception ex, string entityType, string entityId, string activityType);
}

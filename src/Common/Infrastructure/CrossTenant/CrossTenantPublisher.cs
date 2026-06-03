namespace Stratum.Common.Infrastructure.CrossTenant;

using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.BlobStorage;
using Stratum.Common.Abstractions.CrossTenant;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Publishes cross-tenant events by inserting rows into <c>outbox.cross_tenant_events</c>
/// in the system database with status <c>pending</c>.
/// </summary>
public sealed partial class CrossTenantPublisher : ICrossTenantPublisher
{
    private const string InsertSql = """
        INSERT INTO outbox.cross_tenant_events
            (id, source_tenant, target_tenant, event_type, payload, blob_refs, submitter_email, status, retry_count)
        VALUES
            (@Id, @SourceTenant, @TargetTenant, @EventType, @Payload::jsonb, @BlobRefs::jsonb, @SubmitterEmail, 'pending', 0)
        """;

    /// <summary>
    /// PascalCase 3-segment event type: {Module}.{Aggregate}.{Verb}.
    /// </summary>
    private static readonly Regex EventTypePattern = new(
        @"^[A-Z][a-zA-Z]+\.[A-Z][a-zA-Z]+\.[A-Z][a-zA-Z]+$",
        RegexOptions.Compiled);

    private readonly ISystemConnectionFactory _connectionFactory;
    private readonly ILogger<CrossTenantPublisher> _logger;

    public CrossTenantPublisher(
        ISystemConnectionFactory connectionFactory,
        ILogger<CrossTenantPublisher> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task PublishAsync(
        string? sourceTenant,
        string targetTenant,
        string eventType,
        object payload,
        IReadOnlyList<BlobReference>? blobs = null,
        string? submitterEmail = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        if (!EventTypePattern.IsMatch(eventType))
        {
            throw new ArgumentException(
                $"Event type '{eventType}' does not match the required format '{{Module}}.{{Aggregate}}.{{Verb}}' (PascalCase, 3 segments).",
                nameof(eventType));
        }

        if (sourceTenant is null && submitterEmail is null)
        {
            throw new ArgumentException(
                "Public submissions (sourceTenant is null) must provide a submitterEmail.",
                nameof(submitterEmail));
        }

        var id = Guid.NewGuid();
        var payloadJson = JsonSerializer.Serialize(payload, CrossTenantJsonOptions.Instance);
        var blobRefsJson = blobs is { Count: > 0 }
            ? JsonSerializer.Serialize(blobs, CrossTenantJsonOptions.Instance)
            : null;

        var parameters = new
        {
            Id = id,
            SourceTenant = sourceTenant,
            TargetTenant = targetTenant,
            EventType = eventType,
            Payload = payloadJson,
            BlobRefs = blobRefsJson,
            SubmitterEmail = submitterEmail,
        };

        using var connection = await _connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, parameters, cancellationToken: ct));

        LogEventPublished(_logger, eventType, id, sourceTenant, targetTenant);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Cross-tenant event published: {EventType} ({EventId}) from={SourceTenant} to={TargetTenant}")]
    private static partial void LogEventPublished(
        ILogger logger, string eventType, Guid eventId, string? sourceTenant, string targetTenant);
}

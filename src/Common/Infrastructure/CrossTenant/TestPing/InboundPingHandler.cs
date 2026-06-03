namespace Stratum.Common.Infrastructure.CrossTenant.TestPing;

using Dapper;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.CrossTenant;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Handles <c>Test.Ping.Sent</c> events by writing an <c>InboundPing</c> row
/// into the target tenant's <c>crossdata.inbound_pings</c> table.
/// Idempotent: uses <c>ON CONFLICT (event_id) DO NOTHING</c> to prevent duplicates.
/// </summary>
public sealed partial class InboundPingHandler : ICrossTenantHandler<PingPayload>
{
    private const string InsertSql = """
        INSERT INTO crossdata.inbound_pings (event_id, source_tenant, message, submitter_email)
        VALUES (@EventId, @SourceTenant, @Message, @SubmitterEmail)
        ON CONFLICT (event_id) DO NOTHING
        """;

    private readonly ITenantConnectionFactory _tenantConnectionFactory;
    private readonly ILogger<InboundPingHandler> _logger;

    public InboundPingHandler(
        ITenantConnectionFactory tenantConnectionFactory,
        ILogger<InboundPingHandler> logger)
    {
        _tenantConnectionFactory = tenantConnectionFactory;
        _logger = logger;
    }

    public string EventType => "Test.Ping.Sent";

    public async Task HandleAsync(
        CrossTenantEnvelope envelope,
        PingPayload payload,
        CancellationToken ct)
    {
        using var connection = await _tenantConnectionFactory.OpenAsync(envelope.TargetTenant, ct);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                InsertSql,
                new
                {
                    EventId = envelope.Id,
                    envelope.SourceTenant,
                    payload.Message,
                    envelope.SubmitterEmail,
                },
                cancellationToken: ct));

        if (affected > 0)
        {
            LogPingReceived(_logger, envelope.Id, envelope.SourceTenant, envelope.TargetTenant);
        }
        else
        {
            LogPingDuplicate(_logger, envelope.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "InboundPing received: {EventId} from={SourceTenant} to={TargetTenant}")]
    private static partial void LogPingReceived(
        ILogger logger, Guid eventId, string? sourceTenant, string targetTenant);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "InboundPing duplicate skipped: {EventId}")]
    private static partial void LogPingDuplicate(ILogger logger, Guid eventId);
}

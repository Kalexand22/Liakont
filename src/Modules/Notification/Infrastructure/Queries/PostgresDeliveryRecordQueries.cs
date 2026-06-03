namespace Stratum.Modules.Notification.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

public sealed class PostgresDeliveryRecordQueries : IDeliveryRecordQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresDeliveryRecordQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DeliveryRecordDto>> ListByEntity(string entityType, string entityId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, notification_id, template_code, recipient_email, entity_type, entity_id,
                   sent_at, delivered_at, failed_at, retry_count, sla_breached, company_id
            FROM notification.delivery_records
            WHERE entity_type = @EntityType AND entity_id = @EntityId
            ORDER BY sent_at DESC
            LIMIT 200
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { EntityType = entityType, EntityId = entityId }, cancellationToken: ct));

        return MapRows(rows);
    }

    public async Task<IReadOnlyList<DeliveryRecordDto>> ListSlaBreaches(Guid? companyId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, notification_id, template_code, recipient_email, entity_type, entity_id,
                   sent_at, delivered_at, failed_at, retry_count, sla_breached, company_id
            FROM notification.delivery_records
            WHERE sla_breached = true
              AND (company_id IS NULL OR company_id = @CompanyId)
            ORDER BY sent_at DESC
            LIMIT 100
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        return MapRows(rows);
    }

    public async Task<IReadOnlyList<DeliveryRecordDto>> ListFailedForRetry(int maxRetryCount, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, notification_id, template_code, recipient_email, entity_type, entity_id,
                   sent_at, delivered_at, failed_at, retry_count, sla_breached, company_id
            FROM notification.delivery_records
            WHERE failed_at IS NOT NULL AND delivered_at IS NULL AND retry_count < @MaxRetryCount
            ORDER BY failed_at ASC
            LIMIT 50
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { MaxRetryCount = maxRetryCount }, cancellationToken: ct));

        return MapRows(rows);
    }

    private static List<DeliveryRecordDto> MapRows(IEnumerable<dynamic> rows)
    {
        var result = new List<DeliveryRecordDto>();
        foreach (var r in rows)
        {
            result.Add(MapDto(r));
        }

        return result;
    }

    private static DeliveryRecordDto MapDto(dynamic row)
    {
        return new DeliveryRecordDto
        {
            Id = (Guid)row.id,
            NotificationId = (Guid?)row.notification_id,
            TemplateCode = (string)row.template_code,
            RecipientEmail = (string)row.recipient_email,
            EntityType = (string?)row.entity_type,
            EntityId = (string?)row.entity_id,
            SentAt = (DateTimeOffset)row.sent_at,
            DeliveredAt = (DateTimeOffset?)row.delivered_at,
            FailedAt = (DateTimeOffset?)row.failed_at,
            RetryCount = (int)row.retry_count,
            SlaBreached = (bool)row.sla_breached,
            CompanyId = (Guid?)row.company_id,
        };
    }
}

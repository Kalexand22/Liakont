namespace Stratum.Modules.Notification.Infrastructure.Queries;

using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

public sealed class PostgresWebhookQueries : IWebhookQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresWebhookQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<WebhookSubscriptionDto>> ListByEventType(string eventType, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, name, event_type, target_url, is_active, company_id, created_at, updated_at
            FROM notification.webhook_subscriptions
            WHERE event_type = @EventType AND is_active = true
            ORDER BY created_at
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { EventType = eventType }, cancellationToken: ct));

        return rows.Select(MapRow).ToList();
    }

    public async Task<WebhookSubscriptionDto?> GetById(Guid subscriptionId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, name, event_type, target_url, is_active, company_id, created_at, updated_at
            FROM notification.webhook_subscriptions
            WHERE id = @Id
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = subscriptionId }, cancellationToken: ct));

        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<WebhookSubscriptionDto>> ListByCompany(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, name, event_type, target_url, is_active, company_id, created_at, updated_at
            FROM notification.webhook_subscriptions
            WHERE company_id = @CompanyId
            ORDER BY created_at
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        return rows.Select(MapRow).ToList();
    }

    private static WebhookSubscriptionDto MapRow(dynamic r) => new()
    {
        Id = (Guid)r.id,
        Name = (string)r.name,
        EventType = (string)r.event_type,
        TargetUrl = (string)r.target_url,
        IsActive = (bool)r.is_active,
        CompanyId = (Guid)r.company_id,
        CreatedAt = DbTimestamp.ToDateTimeOffset((object)r.created_at),
        UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)r.updated_at),
    };
}

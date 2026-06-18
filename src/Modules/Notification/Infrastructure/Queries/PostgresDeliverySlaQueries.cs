namespace Stratum.Modules.Notification.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

public sealed class PostgresDeliverySlaQueries : IDeliverySlaQueries
{
    private static readonly string[] CategoryNames = ["transactional", "routing", "escalation", "reminder"];

    private readonly IConnectionFactory _connectionFactory;

    public PostgresDeliverySlaQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DeliverySlaDto>> List(Guid? companyId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, category, max_delay_seconds, escalation_action, escalation_recipient, company_id, created_at, updated_at
            FROM notification.delivery_sla
            WHERE (company_id IS NULL OR company_id = @CompanyId)
            ORDER BY category
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        var result = new List<DeliverySlaDto>();
        foreach (var r in rows)
        {
            result.Add(MapDto(r));
        }

        return result;
    }

    public async Task<DeliverySlaDto?> GetByCategory(string category, Guid? companyId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        int categoryValue = Array.IndexOf(CategoryNames, category.ToLowerInvariant());
        if (categoryValue < 0)
        {
            return null;
        }

        const string sql = """
            SELECT id, category, max_delay_seconds, escalation_action, escalation_recipient, company_id, created_at, updated_at
            FROM notification.delivery_sla
            WHERE category = @Category
              AND ((company_id IS NULL AND @CompanyId IS NULL) OR company_id = @CompanyId)
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Category = categoryValue, CompanyId = companyId }, cancellationToken: ct));

        return row is null ? null : MapDto(row);
    }

    public async Task<DeliverySlaDto?> GetById(Guid id, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, category, max_delay_seconds, escalation_action, escalation_recipient, company_id, created_at, updated_at
            FROM notification.delivery_sla
            WHERE id = @Id
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));

        return row is null ? null : MapDto(row);
    }

    private static DeliverySlaDto MapDto(dynamic row)
    {
        int cat = (int)(short)row.category;
        return new DeliverySlaDto
        {
            Id = (Guid)row.id,
            Category = cat >= 0 && cat < CategoryNames.Length ? CategoryNames[cat] : cat.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MaxDelaySeconds = (int)row.max_delay_seconds,
            EscalationAction = (string?)row.escalation_action,
            EscalationRecipient = (string?)row.escalation_recipient,
            CompanyId = (Guid?)row.company_id,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)row.created_at),
            UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)row.updated_at),
        };
    }
}

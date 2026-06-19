namespace Stratum.Modules.Notification.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

public sealed class PostgresRoutingRuleQueries : IRoutingRuleQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresRoutingRuleQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<RoutingRuleDto>> List(Guid? companyId = null, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, code, name, entity_type, service_code, recipient_type, recipient_value, conditions, priority, is_active, company_id, created_at, updated_at
            FROM notification.routing_rules
            WHERE (@CompanyId IS NULL OR company_id = @CompanyId)
            ORDER BY priority ASC
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        return rows.Select(r => (RoutingRuleDto)MapDto(r)).ToList();
    }

    public async Task<IReadOnlyList<RoutingRuleDto>> ListByEntityType(string entityType, Guid? companyId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, code, name, entity_type, service_code, recipient_type, recipient_value, conditions, priority, is_active, company_id, created_at, updated_at
            FROM notification.routing_rules
            WHERE entity_type = @EntityType
              AND (company_id IS NULL OR company_id = @CompanyId)
            ORDER BY priority ASC
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { EntityType = entityType, CompanyId = companyId }, cancellationToken: ct));

        return rows.Select(r => (RoutingRuleDto)MapDto(r)).ToList();
    }

    public async Task<RoutingRuleDto?> GetByCode(string code, string entityType, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, code, name, entity_type, service_code, recipient_type, recipient_value, conditions, priority, is_active, company_id, created_at, updated_at
            FROM notification.routing_rules
            WHERE code = @Code AND entity_type = @EntityType
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Code = code, EntityType = entityType }, cancellationToken: ct));

        return row is null ? null : MapDto(row);
    }

    public async Task<RoutingRuleDto?> GetById(Guid id, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, code, name, entity_type, service_code, recipient_type, recipient_value, conditions, priority, is_active, company_id, created_at, updated_at
            FROM notification.routing_rules
            WHERE id = @Id
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));

        return row is null ? null : MapDto(row);
    }

    private static RoutingRuleDto MapDto(dynamic row)
    {
        var recipientType = (Domain.Entities.RecipientType)(int)(short)row.recipient_type;
        return new RoutingRuleDto
        {
            Id = (Guid)row.id,
            Code = (string)row.code,
            Name = (string)row.name,
            EntityType = (string)row.entity_type,
            ServiceCode = (string)row.service_code,
            RecipientType = recipientType.ToString(),
            RecipientValue = (string)row.recipient_value,
            ConditionsJson = (string)row.conditions,
            Priority = (int)row.priority,
            IsActive = (bool)row.is_active,
            CompanyId = (Guid?)row.company_id,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)row.created_at),
            UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)row.updated_at),
        };
    }
}

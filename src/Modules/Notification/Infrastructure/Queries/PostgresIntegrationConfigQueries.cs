namespace Stratum.Modules.Notification.Infrastructure.Queries;

using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

public sealed class PostgresIntegrationConfigQueries : IIntegrationConfigQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresIntegrationConfigQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IntegrationConfigDto?> GetByType(string integrationType, Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, integration_type, config_json, is_enabled, company_id, created_at, updated_at
            FROM notification.integration_configs
            WHERE integration_type = @IntegrationType AND company_id = @CompanyId
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { IntegrationType = integrationType, CompanyId = companyId }, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return new IntegrationConfigDto
        {
            Id = (Guid)row.id,
            IntegrationType = (string)row.integration_type,
            ConfigJson = (string)row.config_json,
            IsEnabled = (bool)row.is_enabled,
            CompanyId = (Guid)row.company_id,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)row.created_at),
            UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)row.updated_at),
        };
    }

    public async Task<IReadOnlyList<IntegrationConfigDto>> ListByCompany(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, integration_type, config_json, is_enabled, company_id, created_at, updated_at
            FROM notification.integration_configs
            WHERE company_id = @CompanyId
            ORDER BY integration_type
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        return rows.Select(r => new IntegrationConfigDto
        {
            Id = (Guid)r.id,
            IntegrationType = (string)r.integration_type,
            ConfigJson = (string)r.config_json,
            IsEnabled = (bool)r.is_enabled,
            CompanyId = (Guid)r.company_id,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)r.created_at),
            UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)r.updated_at),
        }).ToList();
    }
}

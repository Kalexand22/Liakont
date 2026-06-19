namespace Stratum.Modules.Notification.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

public sealed class PostgresServiceDefinitionQueries : IServiceDefinitionQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresServiceDefinitionQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ServiceDefinitionDto>> List(Guid? companyId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, code, name, email, description, is_active, company_id,
                   manager_name, default_sla_hours, color, competences,
                   created_at, updated_at
            FROM notification.service_definitions
            WHERE (company_id IS NULL OR company_id = @CompanyId)
            ORDER BY code
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        return rows.Select(r => (ServiceDefinitionDto)MapDto(r)).ToList();
    }

    public async Task<ServiceDefinitionDto?> GetByCode(string code, Guid? companyId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, code, name, email, description, is_active, company_id,
                   manager_name, default_sla_hours, color, competences,
                   created_at, updated_at
            FROM notification.service_definitions
            WHERE code = @Code
              AND (company_id IS NULL OR company_id = @CompanyId)
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Code = code, CompanyId = companyId }, cancellationToken: ct));

        return row is null ? null : MapDto(row);
    }

    public async Task<ServiceDefinitionDto?> GetById(Guid id, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, code, name, email, description, is_active, company_id,
                   manager_name, default_sla_hours, color, competences,
                   created_at, updated_at
            FROM notification.service_definitions
            WHERE id = @Id
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));

        return row is null ? null : MapDto(row);
    }

    private static ServiceDefinitionDto MapDto(dynamic row)
    {
        return new ServiceDefinitionDto
        {
            Id = (Guid)row.id,
            Code = (string)row.code,
            Name = (string)row.name,
            Email = (string)row.email,
            Description = (string?)row.description,
            IsActive = (bool)row.is_active,
            CompanyId = (Guid?)row.company_id,
            ManagerName = (string?)row.manager_name,
            DefaultSlaHours = (int?)row.default_sla_hours,
            Color = (string?)row.color,
            Competences = (string?)row.competences,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)row.created_at),
            UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)row.updated_at),
        };
    }
}

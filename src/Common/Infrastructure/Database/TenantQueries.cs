namespace Stratum.Common.Infrastructure.Database;

using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Dapper-based implementation of <see cref="ITenantQueries"/>
/// reading from the <c>outbox.tenants</c> system table.
/// </summary>
public sealed class TenantQueries : ITenantQueries
{
    private const string SelectSql = """
        SELECT id, display_name AS displayname, admin_email AS adminemail,
               database_name AS databasename, realm_name AS realmname,
               is_active AS isactive, provisioned_at AS provisionedat,
               company_id AS companyid
        FROM outbox.tenants
        """;

    private readonly string _connectionString;

    public TenantQueries(IOptions<DatabaseOptions> databaseOptions)
    {
        _connectionString = databaseOptions.Value.ConnectionString;
    }

    public async Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"{SelectSql} ORDER BY provisioned_at DESC";

        var rows = await connection.QueryAsync<TenantRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return rows.Select(MapToDto).ToList();
    }

    public async Task<TenantDto?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"{SelectSql} WHERE id = @TenantId";

        var row = await connection.QuerySingleOrDefaultAsync<TenantRow>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken));

        return row is null ? null : MapToDto(row);
    }

    private static TenantDto MapToDto(TenantRow row) => new()
    {
        Id = row.Id,
        DisplayName = row.DisplayName,
        AdminEmail = row.AdminEmail,
        DatabaseName = row.DatabaseName,
        RealmName = row.RealmName,
        IsActive = row.IsActive,
        ProvisionedAt = row.ProvisionedAt,
        CompanyId = row.CompanyId,
    };

    /// <summary>
    /// Typed Dapper mapping target. Column aliases in the SQL map snake_case
    /// to PascalCase property names (Dapper is case-insensitive by default).
    /// </summary>
    private sealed class TenantRow
    {
        public string Id { get; init; } = default!;

        public string DisplayName { get; init; } = default!;

        public string AdminEmail { get; init; } = default!;

        public string DatabaseName { get; init; } = default!;

        public string? RealmName { get; init; }

        public bool IsActive { get; init; }

        public DateTimeOffset ProvisionedAt { get; init; }

        public Guid? CompanyId { get; init; }
    }
}

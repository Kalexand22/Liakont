namespace Stratum.Modules.Identity.Infrastructure.Repositories;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Domain.Repositories;

internal sealed class PostgresRoleRepository : IRoleRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresRoleRepository(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Role?> GetById(Guid id, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, name, description, is_system, created_at
            FROM identity.roles
            WHERE id = @Id
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));

        return row is null ? null : MapRole(row);
    }

    public async Task<Role?> GetByName(string name, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, name, description, is_system, created_at
            FROM identity.roles
            WHERE name = @Name
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Name = name }, cancellationToken: ct));

        return row is null ? null : MapRole(row);
    }

    public async Task<IReadOnlyList<Role>> GetAll(CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, name, description, is_system, created_at
            FROM identity.roles
            ORDER BY name
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: ct));

        var result = new List<Role>();
        foreach (var r in rows)
        {
            result.Add(MapRole(r));
        }

        return result;
    }

    public async Task Insert(Role role, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            INSERT INTO identity.roles (id, name, description, is_system, created_at)
            VALUES (@Id, @Name, @Description, @IsSystem, @CreatedAt)
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                role.Id,
                role.Name,
                role.Description,
                role.IsSystem,
                role.CreatedAt,
            },
            cancellationToken: ct));
    }

    private static Role MapRole(dynamic row)
    {
        return Role.Reconstitute(
            (Guid)row.id,
            (string)row.name,
            (string?)row.description,
            (bool)row.is_system,
            new DateTimeOffset((DateTime)row.created_at, TimeSpan.Zero));
    }
}

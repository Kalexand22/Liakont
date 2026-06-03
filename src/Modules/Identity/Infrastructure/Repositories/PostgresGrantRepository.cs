namespace Stratum.Modules.Identity.Infrastructure.Repositories;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Domain.Repositories;

internal sealed class PostgresGrantRepository : IGrantRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresGrantRepository(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Grant>> GetByRoleId(Guid roleId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, role_id, permission, module_source, condition, created_at
            FROM identity.grants
            WHERE role_id = @RoleId
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { RoleId = roleId }, cancellationToken: ct));

        var result = new List<Grant>();
        foreach (var r in rows)
        {
            result.Add(Grant.Reconstitute(
                (Guid)r.id,
                (Guid)r.role_id,
                (string)r.permission,
                (string)r.module_source,
                (string?)r.condition,
                new DateTimeOffset((DateTime)r.created_at, TimeSpan.Zero)));
        }

        return result;
    }

    public async Task Insert(Grant grant, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            INSERT INTO identity.grants (id, role_id, permission, module_source, condition, created_at)
            VALUES (@Id, @RoleId, @Permission, @ModuleSource, @Condition, @CreatedAt)
            ON CONFLICT (role_id, permission) DO NOTHING
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                grant.Id,
                grant.RoleId,
                grant.Permission,
                grant.ModuleSource,
                grant.Condition,
                grant.CreatedAt,
            },
            cancellationToken: ct));
    }

    public async Task Delete(Guid roleId, string permission, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            DELETE FROM identity.grants
            WHERE role_id = @RoleId AND permission = @Permission
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new { RoleId = roleId, Permission = permission },
            cancellationToken: ct));
    }
}

namespace Stratum.Modules.Identity.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;

internal sealed class PostgresIdentityQueries : IIdentityQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresIdentityQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<UserDto>> ListUsers(CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT u.id, u.username, u.email, u.display_name, u.party_id,
                   u.is_active, u.last_login_at, u.external_id,
                   COALESCE(string_agg(r.name, ',' ORDER BY r.name), '') AS role_names
            FROM identity.users u
            LEFT JOIN identity.user_roles ur ON ur.user_id = u.id
            LEFT JOIN identity.roles r ON r.id = ur.role_id
            GROUP BY u.id
            ORDER BY u.display_name, u.username
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: ct));

        return rows.Select(r => new UserDto
        {
            Id = (Guid)r.id,
            Username = (string)r.username,
            Email = (string)r.email,
            DisplayName = (string)r.display_name,
            PartyId = (Guid?)r.party_id,
            ExternalId = (string?)r.external_id,
            IsActive = (bool)r.is_active,
            LastLoginAt = r.last_login_at is null
                ? null
                : new DateTimeOffset((DateTime)r.last_login_at, TimeSpan.Zero),
            Roles = string.IsNullOrEmpty((string)r.role_names)
                ? []
                : ((string)r.role_names).Split(',').ToList(),
        }).ToList();
    }

    public async Task<UserDto?> GetUserById(Guid userId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);
        return await LoadUserDtoAsync(conn, "WHERE u.id = @Param", new { Param = userId }, ct);
    }

    public async Task<UserDto?> GetUserByUsername(string username, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);
        return await LoadUserDtoAsync(conn, "WHERE u.username = @Param", new { Param = username }, ct);
    }

    public async Task<UserDto?> GetUserByEmail(string email, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);
        return await LoadUserDtoAsync(conn, "WHERE u.email = @Param", new { Param = email }, ct);
    }

    public async Task<IReadOnlyList<string>> GetUserPermissions(Guid userId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT DISTINCT g.permission
            FROM identity.grants g
            INNER JOIN identity.user_roles ur ON ur.role_id = g.role_id
            WHERE ur.user_id = @UserId
            ORDER BY g.permission
            """;

        var rows = await conn.QueryAsync<string>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<bool> UserHasPermission(Guid userId, string permission, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.grants g
                INNER JOIN identity.user_roles ur ON ur.role_id = g.role_id
                WHERE ur.user_id = @UserId AND g.permission = @Permission
            )
            """;

        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { UserId = userId, Permission = permission }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<GrantConditionDto>> GetUserGrantsForPermission(Guid userId, string permission, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT g.id AS grant_id, g.permission, g.condition
            FROM identity.grants g
            INNER JOIN identity.user_roles ur ON ur.role_id = g.role_id
            WHERE ur.user_id = @UserId AND g.permission = @Permission
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { UserId = userId, Permission = permission }, cancellationToken: ct));

        return rows.Select(r => new GrantConditionDto
        {
            GrantId = (Guid)r.grant_id,
            Permission = (string)r.permission,
            Condition = (string?)r.condition,
        }).ToList();
    }

    public async Task<IReadOnlyList<RoleDto>> GetRoles(CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, name, description, is_system
            FROM identity.roles
            ORDER BY name
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: ct));

        var result = new List<RoleDto>();
        foreach (var r in rows)
        {
            result.Add(new RoleDto
            {
                Id = (Guid)r.id,
                Name = (string)r.name,
                Description = (string?)r.description,
                IsSystem = (bool)r.is_system,
            });
        }

        return result;
    }

    public async Task<RoleDetailDto?> GetRoleById(Guid roleId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string roleSql = """
            SELECT id, name, description, is_system, created_at
            FROM identity.roles
            WHERE id = @RoleId
            """;

        var roleRow = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(roleSql, new { RoleId = roleId }, cancellationToken: ct));

        if (roleRow is null)
        {
            return null;
        }

        const string grantsSql = """
            SELECT permission
            FROM identity.grants
            WHERE role_id = @RoleId
            ORDER BY permission
            """;

        var grants = await conn.QueryAsync<string>(
            new CommandDefinition(grantsSql, new { RoleId = roleId }, cancellationToken: ct));

        return new RoleDetailDto
        {
            Id = (Guid)roleRow.id,
            Name = (string)roleRow.name,
            Description = (string?)roleRow.description,
            IsSystem = (bool)roleRow.is_system,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)roleRow.created_at),
            GrantedPermissions = grants.ToList(),
        };
    }

    public async Task<IReadOnlyList<RoleUserDto>> GetUsersForRole(Guid roleId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT u.id AS user_id, u.username, u.display_name, u.email, u.is_active
            FROM identity.users u
            INNER JOIN identity.user_roles ur ON ur.user_id = u.id
            WHERE ur.role_id = @RoleId
            ORDER BY u.display_name, u.username
            """;

        var rows = await conn.QueryAsync(
            new CommandDefinition(sql, new { RoleId = roleId }, cancellationToken: ct));

        return rows.Select(r => new RoleUserDto
        {
            UserId = (Guid)r.user_id,
            Username = (string)r.username,
            DisplayName = (string)r.display_name,
            Email = (string)r.email,
            IsActive = (bool)r.is_active,
        }).ToList();
    }

    private static async Task<UserDto?> LoadUserDtoAsync(
        IDbConnection conn,
        string whereClause,
        object parameters,
        CancellationToken ct)
    {
        var userSql = $"""
            SELECT u.id, u.username, u.email, u.display_name, u.party_id, u.is_active, u.last_login_at, u.external_id
            FROM identity.users u
            {whereClause}
            """;

        var userRow = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(userSql, parameters, cancellationToken: ct));

        if (userRow is null)
        {
            return null;
        }

        Guid userId = (Guid)userRow.id;

        const string rolesSql = """
            SELECT r.name
            FROM identity.roles r
            INNER JOIN identity.user_roles ur ON ur.role_id = r.id
            WHERE ur.user_id = @UserId
            ORDER BY r.name
            """;

        var roleNames = await conn.QueryAsync<string>(
            new CommandDefinition(rolesSql, new { UserId = userId }, cancellationToken: ct));

        return new UserDto
        {
            Id = userId,
            Username = (string)userRow.username,
            Email = (string)userRow.email,
            DisplayName = (string)userRow.display_name,
            PartyId = (Guid?)userRow.party_id,
            ExternalId = (string?)userRow.external_id,
            IsActive = (bool)userRow.is_active,
            LastLoginAt = userRow.last_login_at is null
                ? null
                : new DateTimeOffset((DateTime)userRow.last_login_at, TimeSpan.Zero),
            Roles = roleNames.ToList(),
        };
    }
}

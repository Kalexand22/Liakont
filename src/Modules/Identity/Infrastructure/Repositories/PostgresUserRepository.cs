namespace Stratum.Modules.Identity.Infrastructure.Repositories;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Domain.Repositories;

internal sealed class PostgresUserRepository : IUserRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresUserRepository(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> GetById(Guid id, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);
        return await LoadUserAsync(conn, "WHERE u.id = @Id", new { Id = id }, ct);
    }

    public async Task<User?> GetByUsername(string username, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);
        return await LoadUserAsync(conn, "WHERE u.username = @Username", new { Username = username }, ct);
    }

    public async Task<User?> GetByExternalId(string externalId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);
        return await LoadUserAsync(conn, "WHERE u.external_id = @ExternalId", new { ExternalId = externalId }, ct);
    }

    public async Task<User?> GetByEmail(string email, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);
        return await LoadUserAsync(conn, "WHERE u.email = @Email", new { Email = email }, ct);
    }

    public async Task Insert(User user, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            INSERT INTO identity.users
                (id, username, email, display_name, password_hash, party_id, is_active, last_login_at, created_at, updated_at, external_id)
            VALUES
                (@Id, @Username, @Email, @DisplayName, @PasswordHash, @PartyId, @IsActive, @LastLoginAt, @CreatedAt, @UpdatedAt, @ExternalId)
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                user.Id,
                Username = user.Username.Value,
                Email = user.Email.Value,
                DisplayName = user.DisplayName ?? string.Empty,
                user.PasswordHash,
                user.PartyId,
                user.IsActive,
                user.LastLoginAt,
                user.CreatedAt,
                user.UpdatedAt,
                user.ExternalId,
            },
            cancellationToken: ct));
    }

    public async Task Update(User user, CancellationToken ct = default)
    {
        await using var txn = await TransactionScope.BeginAsync(_connectionFactory, ct);

        const string updateSql = """
            UPDATE identity.users
            SET email         = @Email,
                display_name  = @DisplayName,
                password_hash = @PasswordHash,
                is_active     = @IsActive,
                last_login_at = @LastLoginAt,
                updated_at    = @UpdatedAt,
                external_id   = @ExternalId
            WHERE id = @Id
            """;

        await txn.Connection.ExecuteAsync(new CommandDefinition(
            updateSql,
            new
            {
                user.Id,
                Email = user.Email.Value,
                DisplayName = user.DisplayName ?? string.Empty,
                user.PasswordHash,
                user.IsActive,
                user.LastLoginAt,
                user.UpdatedAt,
                user.ExternalId,
            },
            txn.Transaction,
            cancellationToken: ct));

        const string deleteRolesSql = "DELETE FROM identity.user_roles WHERE user_id = @UserId";
        await txn.Connection.ExecuteAsync(new CommandDefinition(
            deleteRolesSql,
            new { UserId = user.Id },
            txn.Transaction,
            cancellationToken: ct));

        if (user.Roles.Count > 0)
        {
            const string insertRoleSql = """
                INSERT INTO identity.user_roles (id, user_id, role_id, granted_at)
                VALUES (@Id, @UserId, @RoleId, @GrantedAt)
                """;

            foreach (var role in user.Roles)
            {
                await txn.Connection.ExecuteAsync(new CommandDefinition(
                    insertRoleSql,
                    new { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id, GrantedAt = DateTimeOffset.UtcNow },
                    txn.Transaction,
                    cancellationToken: ct));
            }
        }

        await txn.CommitAsync(ct);
    }

    private static async Task<User?> LoadUserAsync(
        IDbConnection conn,
        string whereClause,
        object parameters,
        CancellationToken ct)
    {
        var userSql = $"""
            SELECT u.id, u.username, u.email, u.display_name, u.password_hash, u.party_id,
                   u.is_active, u.last_login_at, u.created_at, u.updated_at,
                   u.external_id
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
            SELECT r.id, r.name, r.description, r.is_system, r.created_at
            FROM identity.roles r
            INNER JOIN identity.user_roles ur ON ur.role_id = r.id
            WHERE ur.user_id = @UserId
            """;

        var roleRows = await conn.QueryAsync(
            new CommandDefinition(rolesSql, new { UserId = userId }, cancellationToken: ct));

        var roles = new List<Role>();
        foreach (var r in roleRows)
        {
            roles.Add(Role.Reconstitute(
                (Guid)r.id,
                (string)r.name,
                (string?)r.description,
                (bool)r.is_system,
                new DateTimeOffset((DateTime)r.created_at, TimeSpan.Zero)));
        }

        string? displayName = (string?)userRow.display_name is { Length: > 0 } dn ? dn : null;
        DateTimeOffset? lastLoginAt = userRow.last_login_at is null
            ? null
            : new DateTimeOffset((DateTime)userRow.last_login_at, TimeSpan.Zero);
        DateTimeOffset createdAt = new DateTimeOffset((DateTime)userRow.created_at, TimeSpan.Zero);
        DateTimeOffset? updatedAt = userRow.updated_at is null
            ? null
            : new DateTimeOffset((DateTime)userRow.updated_at, TimeSpan.Zero);

        string? externalId = (string?)userRow.external_id;

        return User.Reconstitute(
            userId,
            (string)userRow.username,
            (string)userRow.email,
            displayName,
            (string)userRow.password_hash,
            (Guid?)userRow.party_id,
            (bool)userRow.is_active,
            lastLoginAt,
            createdAt,
            updatedAt,
            roles,
            externalId);
    }
}

namespace Stratum.Modules.Identity.Infrastructure;

using Dapper;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Domain.Entities;

public sealed class PostgresIdentityUnitOfWork : IIdentityUnitOfWork
{
    private readonly IOutboxWriter _outboxWriter;

    private readonly TransactionScope _txn;

    private PostgresIdentityUnitOfWork(TransactionScope txn, IOutboxWriter outboxWriter)
    {
        _txn = txn;
        _outboxWriter = outboxWriter;
    }

    public static async Task<PostgresIdentityUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        IOutboxWriter outboxWriter,
        CancellationToken ct = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, ct);
        return new PostgresIdentityUnitOfWork(txn, outboxWriter);
    }

    public async Task InsertUserAsync(User user, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO identity.users
                (id, username, email, display_name, password_hash, party_id, is_active, created_at, updated_at, external_id)
            VALUES
                (@Id, @Username, @Email, @DisplayName, @PasswordHash, @PartyId, @IsActive, @CreatedAt, @UpdatedAt, @ExternalId)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                user.Id,
                Username = user.Username.Value,
                Email = user.Email.Value,
                user.DisplayName,
                user.PasswordHash,
                user.PartyId,
                user.IsActive,
                user.CreatedAt,
                user.UpdatedAt,
                user.ExternalId,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateUserAsync(User user, CancellationToken ct = default)
    {
        const string updateSql = """
            UPDATE identity.users
            SET username      = @Username,
                email         = @Email,
                display_name  = @DisplayName,
                password_hash = @PasswordHash,
                is_active     = @IsActive,
                last_login_at = @LastLoginAt,
                updated_at    = @UpdatedAt,
                external_id   = @ExternalId
            WHERE id = @Id
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            updateSql,
            new
            {
                user.Id,
                Username = user.Username.Value,
                Email = user.Email.Value,
                DisplayName = user.DisplayName ?? string.Empty,
                user.PasswordHash,
                user.IsActive,
                user.LastLoginAt,
                user.UpdatedAt,
                user.ExternalId,
            },
            _txn.Transaction,
            cancellationToken: ct));

        const string deleteRolesSql = "DELETE FROM identity.user_roles WHERE user_id = @UserId";

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            deleteRolesSql,
            new { UserId = user.Id },
            _txn.Transaction,
            cancellationToken: ct));

        const string insertRoleSql = """
            INSERT INTO identity.user_roles (id, user_id, role_id, granted_at)
            VALUES (@Id, @UserId, @RoleId, @GrantedAt)
            """;

        foreach (var role in user.Roles)
        {
            await _txn.Connection.ExecuteAsync(new CommandDefinition(
                insertRoleSql,
                new { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id, GrantedAt = DateTimeOffset.UtcNow },
                _txn.Transaction,
                cancellationToken: ct));
        }
    }

    public async Task UpdateLastLoginAsync(Guid userId, DateTimeOffset lastLoginAt, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE identity.users
            SET last_login_at = @LastLoginAt,
                updated_at    = @UpdatedAt
            WHERE id = @Id
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = userId, LastLoginAt = lastLoginAt, UpdatedAt = DateTimeOffset.UtcNow },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task InsertRoleAsync(Role role, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO identity.roles (id, name, description, is_system, created_at)
            VALUES (@Id, @Name, @Description, @IsSystem, @CreatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { role.Id, role.Name, role.Description, role.IsSystem, role.CreatedAt },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateRoleAsync(Role role, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE identity.roles
            SET name = @Name, description = @Description
            WHERE id = @Id
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { role.Id, role.Name, role.Description },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM identity.grants WHERE role_id = @Id",
            new { Id = roleId },
            _txn.Transaction,
            cancellationToken: ct));

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM identity.user_roles WHERE role_id = @Id",
            new { Id = roleId },
            _txn.Transaction,
            cancellationToken: ct));

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM identity.roles WHERE id = @Id",
            new { Id = roleId },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task InsertGrantAsync(Grant grant, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO identity.grants (id, role_id, permission, module_source, condition, created_at)
            VALUES (@Id, @RoleId, @Permission, @ModuleSource, @Condition, @CreatedAt)
            ON CONFLICT (role_id, permission) DO NOTHING
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { grant.Id, grant.RoleId, grant.Permission, grant.ModuleSource, grant.Condition, grant.CreatedAt },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task DeleteGrantAsync(Guid roleId, string permission, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM identity.grants
            WHERE role_id = @RoleId AND permission = @Permission
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { RoleId = roleId, Permission = permission },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task<AgentProfile?> GetAgentProfileByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, user_id, service_code, title, phone, office_location, hire_date, notes, created_at, updated_at
            FROM identity.agent_profiles WHERE id = @Id
            """;
        var r = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, _txn.Transaction, cancellationToken: ct));
        return r is null ? null : AgentProfile.Reconstitute(
            (Guid)r.id,
            (Guid)r.user_id,
            (string?)r.service_code,
            (string?)r.title,
            (string?)r.phone,
            (string?)r.office_location,
            r.hire_date is null ? null : DateOnly.FromDateTime((DateTime)r.hire_date),
            (string?)r.notes,
            DbTimestamp.ToDateTimeOffset((object)r.created_at),
            DbTimestamp.ToNullableDateTimeOffset((object?)r.updated_at));
    }

    public async Task<AgentProfile?> GetAgentProfileByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, user_id, service_code, title, phone, office_location, hire_date, notes, created_at, updated_at
            FROM identity.agent_profiles WHERE user_id = @UserId
            """;
        var r = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { UserId = userId }, _txn.Transaction, cancellationToken: ct));
        return r is null ? null : AgentProfile.Reconstitute(
            (Guid)r.id,
            (Guid)r.user_id,
            (string?)r.service_code,
            (string?)r.title,
            (string?)r.phone,
            (string?)r.office_location,
            r.hire_date is null ? null : DateOnly.FromDateTime((DateTime)r.hire_date),
            (string?)r.notes,
            DbTimestamp.ToDateTimeOffset((object)r.created_at),
            DbTimestamp.ToNullableDateTimeOffset((object?)r.updated_at));
    }

    public async Task InsertAgentProfileAsync(AgentProfile profile, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO identity.agent_profiles (id, user_id, service_code, title, phone, office_location, hire_date, notes, created_at, updated_at)
            VALUES (@Id, @UserId, @ServiceCode, @Title, @Phone, @OfficeLocation, @HireDate, @Notes, @CreatedAt, @UpdatedAt)
            """;
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                profile.Id,
                profile.UserId,
                profile.ServiceCode,
                profile.Title,
                profile.Phone,
                profile.OfficeLocation,
                HireDate = profile.HireDate?.ToDateTime(TimeOnly.MinValue),
                profile.Notes,
                profile.CreatedAt,
                profile.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateAgentProfileAsync(AgentProfile profile, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE identity.agent_profiles
            SET service_code = @ServiceCode, title = @Title, phone = @Phone,
                office_location = @OfficeLocation, hire_date = @HireDate,
                notes = @Notes, updated_at = @UpdatedAt
            WHERE id = @Id
            """;
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                profile.Id,
                profile.ServiceCode,
                profile.Title,
                profile.Phone,
                profile.OfficeLocation,
                HireDate = profile.HireDate?.ToDateTime(TimeOnly.MinValue),
                profile.Notes,
                profile.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task<Team?> GetTeamByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, code, name, description, service_code, is_active, created_at, updated_at
            FROM identity.teams WHERE id = @Id
            """;
        var r = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, _txn.Transaction, cancellationToken: ct));
        return r is null ? null : Team.Reconstitute(
            (Guid)r.id,
            (string)r.code,
            (string)r.name,
            (string?)r.description,
            (string?)r.service_code,
            (bool)r.is_active,
            DbTimestamp.ToDateTimeOffset((object)r.created_at),
            DbTimestamp.ToNullableDateTimeOffset((object?)r.updated_at));
    }

    public async Task InsertTeamAsync(Team team, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO identity.teams (id, code, name, description, service_code, is_active, created_at, updated_at)
            VALUES (@Id, @Code, @Name, @Description, @ServiceCode, @IsActive, @CreatedAt, @UpdatedAt)
            """;
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                team.Id,
                team.Code,
                team.Name,
                team.Description,
                team.ServiceCode,
                team.IsActive,
                team.CreatedAt,
                team.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateTeamAsync(Team team, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE identity.teams
            SET name = @Name, description = @Description, service_code = @ServiceCode,
                is_active = @IsActive, updated_at = @UpdatedAt
            WHERE id = @Id
            """;
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                team.Id,
                team.Name,
                team.Description,
                team.ServiceCode,
                team.IsActive,
                team.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task DeleteTeamAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM identity.teams WHERE id = @Id";
        await _txn.Connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, _txn.Transaction, cancellationToken: ct));
    }

    public async Task InsertTeamMemberAsync(TeamMember member, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO identity.team_members (id, team_id, user_id, role, joined_at)
            VALUES (@Id, @TeamId, @UserId, @Role, @JoinedAt)
            """;
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                member.Id,
                member.TeamId,
                member.UserId,
                member.Role,
                member.JoinedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task DeleteTeamMemberAsync(Guid memberId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM identity.team_members WHERE id = @Id";
        await _txn.Connection.ExecuteAsync(new CommandDefinition(sql, new { Id = memberId }, _txn.Transaction, cancellationToken: ct));
    }

    public async Task InsertAgentCompetenceAsync(AgentCompetence competence, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO identity.agent_competences (id, user_id, name, category, valid_until, created_at)
            VALUES (@Id, @UserId, @Name, @Category, @ValidUntil, @CreatedAt)
            """;
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                competence.Id,
                competence.UserId,
                competence.Name,
                competence.Category,
                ValidUntil = competence.ValidUntil?.ToDateTime(TimeOnly.MinValue),
                competence.CreatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task DeleteAgentCompetenceAsync(Guid competenceId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM identity.agent_competences WHERE id = @Id";
        await _txn.Connection.ExecuteAsync(new CommandDefinition(sql, new { Id = competenceId }, _txn.Transaction, cancellationToken: ct));
    }

    public async Task<Delegation?> GetDelegationByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, delegator_id, delegate_id, scope, valid_from, valid_until, reason, is_active, created_at
            FROM identity.delegations WHERE id = @Id
            """;
        var r = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, _txn.Transaction, cancellationToken: ct));
        return r is null ? null : Delegation.Reconstitute(
            (Guid)r.id,
            (Guid)r.delegator_id,
            (Guid)r.delegate_id,
            (string)r.scope,
            DbTimestamp.ToDateTimeOffset((object)r.valid_from),
            DbTimestamp.ToDateTimeOffset((object)r.valid_until),
            (string?)r.reason,
            (bool)r.is_active,
            DbTimestamp.ToDateTimeOffset((object)r.created_at));
    }

    public async Task InsertDelegationAsync(Delegation delegation, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO identity.delegations (id, delegator_id, delegate_id, scope, valid_from, valid_until, reason, is_active, created_at)
            VALUES (@Id, @DelegatorId, @DelegateId, @Scope, @ValidFrom, @ValidUntil, @Reason, @IsActive, @CreatedAt)
            """;
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                delegation.Id,
                delegation.DelegatorId,
                delegation.DelegateId,
                delegation.Scope,
                delegation.ValidFrom,
                delegation.ValidUntil,
                delegation.Reason,
                delegation.IsActive,
                delegation.CreatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateDelegationAsync(Delegation delegation, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE identity.delegations SET is_active = @IsActive WHERE id = @Id
            """;
        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { delegation.Id, delegation.IsActive },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task CommitWithEventAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken ct = default)
    {
        await _outboxWriter.WriteAsync(_txn, integrationEvent, ct);
        await _txn.CommitAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _txn.CommitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }
}

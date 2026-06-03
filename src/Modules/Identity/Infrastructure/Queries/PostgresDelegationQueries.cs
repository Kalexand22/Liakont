namespace Stratum.Modules.Identity.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;

internal sealed class PostgresDelegationQueries : IDelegationQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresDelegationQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DelegationDto>> List(CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT d.id, d.delegator_id, u1.display_name AS delegator_name,
                   d.delegate_id, u2.display_name AS delegate_name,
                   d.scope, d.valid_from, d.valid_until, d.reason, d.is_active, d.created_at
            FROM identity.delegations d
            JOIN identity.users u1 ON u1.id = d.delegator_id
            JOIN identity.users u2 ON u2.id = d.delegate_id
            ORDER BY d.valid_from DESC
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r => new DelegationDto
        {
            Id = (Guid)r.id,
            DelegatorId = (Guid)r.delegator_id,
            DelegatorName = (string?)r.delegator_name ?? "—",
            DelegateId = (Guid)r.delegate_id,
            DelegateName = (string?)r.delegate_name ?? "—",
            Scope = (string)r.scope,
            ValidFrom = (DateTimeOffset)r.valid_from,
            ValidUntil = (DateTimeOffset)r.valid_until,
            Reason = (string?)r.reason,
            IsActive = (bool)r.is_active,
            CreatedAt = (DateTimeOffset)r.created_at,
        }).ToList();
    }

    public async Task<DelegationDto?> GetById(Guid delegationId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT d.id, d.delegator_id, u1.display_name AS delegator_name,
                   d.delegate_id, u2.display_name AS delegate_name,
                   d.scope, d.valid_from, d.valid_until, d.reason, d.is_active, d.created_at
            FROM identity.delegations d
            JOIN identity.users u1 ON u1.id = d.delegator_id
            JOIN identity.users u2 ON u2.id = d.delegate_id
            WHERE d.id = @Id
            """;

        var r = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { Id = delegationId }, cancellationToken: ct));
        if (r is null)
        {
            return null;
        }

        return new DelegationDto
        {
            Id = (Guid)r.id,
            DelegatorId = (Guid)r.delegator_id,
            DelegatorName = (string?)r.delegator_name ?? "—",
            DelegateId = (Guid)r.delegate_id,
            DelegateName = (string?)r.delegate_name ?? "—",
            Scope = (string)r.scope,
            ValidFrom = (DateTimeOffset)r.valid_from,
            ValidUntil = (DateTimeOffset)r.valid_until,
            Reason = (string?)r.reason,
            IsActive = (bool)r.is_active,
            CreatedAt = (DateTimeOffset)r.created_at,
        };
    }

    public async Task<IReadOnlyList<DelegationDto>> GetActiveDelegationsForUser(Guid userId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT d.id, d.delegator_id, u1.display_name AS delegator_name,
                   d.delegate_id, u2.display_name AS delegate_name,
                   d.scope, d.valid_from, d.valid_until, d.reason, d.is_active, d.created_at
            FROM identity.delegations d
            JOIN identity.users u1 ON u1.id = d.delegator_id
            JOIN identity.users u2 ON u2.id = d.delegate_id
            WHERE d.is_active = true
              AND (d.delegator_id = @UserId OR d.delegate_id = @UserId)
              AND d.valid_from <= now() AND d.valid_until > now()
            ORDER BY d.valid_from DESC
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
        return rows.Select(r => new DelegationDto
        {
            Id = (Guid)r.id,
            DelegatorId = (Guid)r.delegator_id,
            DelegatorName = (string?)r.delegator_name ?? "—",
            DelegateId = (Guid)r.delegate_id,
            DelegateName = (string?)r.delegate_name ?? "—",
            Scope = (string)r.scope,
            ValidFrom = (DateTimeOffset)r.valid_from,
            ValidUntil = (DateTimeOffset)r.valid_until,
            Reason = (string?)r.reason,
            IsActive = (bool)r.is_active,
            CreatedAt = (DateTimeOffset)r.created_at,
        }).ToList();
    }
}

namespace Stratum.Modules.Identity.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;

internal sealed class PostgresTeamQueries : ITeamQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresTeamQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<TeamDto>> List(CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT t.id, t.code, t.name, t.description, t.service_code,
                   (SELECT count(*) FROM identity.team_members tm WHERE tm.team_id = t.id) AS member_count,
                   t.is_active, t.created_at, t.updated_at
            FROM identity.teams t
            ORDER BY t.name
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r => new TeamDto
        {
            Id = (Guid)r.id,
            Code = (string)r.code,
            Name = (string)r.name,
            Description = (string?)r.description,
            ServiceCode = (string?)r.service_code,
            MemberCount = (int)(long)r.member_count,
            IsActive = (bool)r.is_active,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)r.created_at),
            UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)r.updated_at),
        }).ToList();
    }

    public async Task<TeamDto?> GetById(Guid teamId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT t.id, t.code, t.name, t.description, t.service_code,
                   (SELECT count(*) FROM identity.team_members tm WHERE tm.team_id = t.id) AS member_count,
                   t.is_active, t.created_at, t.updated_at
            FROM identity.teams t
            WHERE t.id = @Id
            """;

        var r = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { Id = teamId }, cancellationToken: ct));
        if (r is null)
        {
            return null;
        }

        return new TeamDto
        {
            Id = (Guid)r.id,
            Code = (string)r.code,
            Name = (string)r.name,
            Description = (string?)r.description,
            ServiceCode = (string?)r.service_code,
            MemberCount = (int)(long)r.member_count,
            IsActive = (bool)r.is_active,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)r.created_at),
            UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)r.updated_at),
        };
    }

    public async Task<IReadOnlyList<TeamMemberDto>> GetMembers(Guid teamId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT tm.id, tm.team_id, tm.user_id, u.username, u.display_name, tm.role, tm.joined_at
            FROM identity.team_members tm
            JOIN identity.users u ON u.id = tm.user_id
            WHERE tm.team_id = @TeamId
            ORDER BY u.display_name, u.username
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { TeamId = teamId }, cancellationToken: ct));
        return rows.Select(r => new TeamMemberDto
        {
            Id = (Guid)r.id,
            TeamId = (Guid)r.team_id,
            UserId = (Guid)r.user_id,
            Username = (string)r.username,
            DisplayName = (string?)r.display_name,
            Role = (string?)r.role,
            JoinedAt = DbTimestamp.ToDateTimeOffset((object)r.joined_at),
        }).ToList();
    }
}

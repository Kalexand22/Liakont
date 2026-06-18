namespace Stratum.Modules.Identity.Infrastructure.Queries;

using System.Data;
using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;

internal sealed class PostgresAgentQueries : IAgentQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresAgentQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AgentDto>> List(CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT ap.id, ap.user_id, u.username, u.email, u.display_name,
                   ap.service_code,
                   ap.title, ap.phone, ap.office_location, ap.hire_date, ap.notes,
                   u.is_active, ap.created_at, ap.updated_at,
                   (SELECT string_agg(t.name, ', ' ORDER BY t.name)
                    FROM identity.team_members tm
                    JOIN identity.teams t ON t.id = tm.team_id
                    WHERE tm.user_id = ap.user_id) AS teams,
                   (SELECT count(*)
                    FROM identity.agent_competences ac
                    WHERE ac.user_id = ap.user_id) AS competence_count
            FROM identity.agent_profiles ap
            JOIN identity.users u ON u.id = ap.user_id
            ORDER BY u.display_name, u.username
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: ct));

        return rows.Select(r => new AgentDto
        {
            Id = (Guid)r.id,
            UserId = (Guid)r.user_id,
            Username = (string)r.username,
            Email = (string)r.email,
            DisplayName = (string?)r.display_name,
            ServiceCode = (string?)r.service_code,
            Title = (string?)r.title,
            Phone = (string?)r.phone,
            OfficeLocation = (string?)r.office_location,
            HireDate = r.hire_date is null ? null : DateOnly.FromDateTime((DateTime)r.hire_date),
            Notes = (string?)r.notes,
            IsActive = (bool)r.is_active,
            Teams = (string?)r.teams,
            CompetenceCount = (int)(long)r.competence_count,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)r.created_at),
            UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)r.updated_at),
        }).ToList();
    }

    public async Task<AgentDto?> GetById(Guid agentProfileId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT ap.id, ap.user_id, u.username, u.email, u.display_name,
                   ap.service_code,
                   ap.title, ap.phone, ap.office_location, ap.hire_date, ap.notes,
                   u.is_active, ap.created_at, ap.updated_at,
                   (SELECT string_agg(t.name, ', ' ORDER BY t.name)
                    FROM identity.team_members tm
                    JOIN identity.teams t ON t.id = tm.team_id
                    WHERE tm.user_id = ap.user_id) AS teams,
                   (SELECT count(*)
                    FROM identity.agent_competences ac
                    WHERE ac.user_id = ap.user_id) AS competence_count
            FROM identity.agent_profiles ap
            JOIN identity.users u ON u.id = ap.user_id
            WHERE ap.id = @Id
            """;

        var r = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { Id = agentProfileId }, cancellationToken: ct));
        if (r is null)
        {
            return null;
        }

        return new AgentDto
        {
            Id = (Guid)r.id,
            UserId = (Guid)r.user_id,
            Username = (string)r.username,
            Email = (string)r.email,
            DisplayName = (string?)r.display_name,
            ServiceCode = (string?)r.service_code,
            Title = (string?)r.title,
            Phone = (string?)r.phone,
            OfficeLocation = (string?)r.office_location,
            HireDate = r.hire_date is null ? null : DateOnly.FromDateTime((DateTime)r.hire_date),
            Notes = (string?)r.notes,
            IsActive = (bool)r.is_active,
            Teams = (string?)r.teams,
            CompetenceCount = (int)(long)r.competence_count,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)r.created_at),
            UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)r.updated_at),
        };
    }

    public async Task<AgentDto?> GetByUserId(Guid userId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT ap.id, ap.user_id, u.username, u.email, u.display_name,
                   ap.service_code,
                   ap.title, ap.phone, ap.office_location, ap.hire_date, ap.notes,
                   u.is_active, ap.created_at, ap.updated_at,
                   (SELECT string_agg(t.name, ', ' ORDER BY t.name)
                    FROM identity.team_members tm
                    JOIN identity.teams t ON t.id = tm.team_id
                    WHERE tm.user_id = ap.user_id) AS teams,
                   (SELECT count(*)
                    FROM identity.agent_competences ac
                    WHERE ac.user_id = ap.user_id) AS competence_count
            FROM identity.agent_profiles ap
            JOIN identity.users u ON u.id = ap.user_id
            WHERE ap.user_id = @UserId
            """;

        var r = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
        if (r is null)
        {
            return null;
        }

        return new AgentDto
        {
            Id = (Guid)r.id,
            UserId = (Guid)r.user_id,
            Username = (string)r.username,
            Email = (string)r.email,
            DisplayName = (string?)r.display_name,
            ServiceCode = (string?)r.service_code,
            Title = (string?)r.title,
            Phone = (string?)r.phone,
            OfficeLocation = (string?)r.office_location,
            HireDate = r.hire_date is null ? null : DateOnly.FromDateTime((DateTime)r.hire_date),
            Notes = (string?)r.notes,
            IsActive = (bool)r.is_active,
            Teams = (string?)r.teams,
            CompetenceCount = (int)(long)r.competence_count,
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)r.created_at),
            UpdatedAt = DbTimestamp.ToNullableDateTimeOffset((object?)r.updated_at),
        };
    }

    public async Task<IReadOnlyList<AgentCompetenceDto>> GetCompetences(Guid userId, CancellationToken ct = default)
    {
        using IDbConnection conn = await _connectionFactory.OpenAsync(ct);

        const string sql = """
            SELECT id, user_id, name, category, valid_until, created_at
            FROM identity.agent_competences
            WHERE user_id = @UserId
            ORDER BY name
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
        return rows.Select(r => new AgentCompetenceDto
        {
            Id = (Guid)r.id,
            UserId = (Guid)r.user_id,
            Name = (string)r.name,
            Category = (string?)r.category,
            ValidUntil = r.valid_until is null ? null : DateOnly.FromDateTime((DateTime)r.valid_until),
            CreatedAt = DbTimestamp.ToDateTimeOffset((object)r.created_at),
        }).ToList();
    }
}

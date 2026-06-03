namespace Stratum.Modules.Notification.Infrastructure.Queries;

using Dapper;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

public sealed class PostgresApiKeyQueries : IApiKeyQueries
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresApiKeyQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ApiKeyDto>> ListByCompany(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, name, key_prefix, scopes, rate_limit, is_revoked,
                   company_id, created_at, revoked_at, expires_at
            FROM notification.api_keys
            WHERE company_id = @CompanyId
            ORDER BY created_at DESC
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));

        return rows.Select(r => new ApiKeyDto
        {
            Id = (Guid)r.id,
            Name = (string)r.name,
            KeyPrefix = (string)r.key_prefix,
            Scopes = (string[])r.scopes,
            RateLimit = (int)r.rate_limit,
            IsRevoked = (bool)r.is_revoked,
            CompanyId = (Guid)r.company_id,
            CreatedAt = (DateTimeOffset)r.created_at,
            RevokedAt = (DateTimeOffset?)r.revoked_at,
            ExpiresAt = (DateTimeOffset?)r.expires_at,
        }).ToList();
    }

    public async Task<ApiKeyDto?> GetById(Guid apiKeyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, name, key_prefix, scopes, rate_limit, is_revoked,
                   company_id, created_at, revoked_at, expires_at
            FROM notification.api_keys
            WHERE id = @Id
            """;

        using var conn = await _connectionFactory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = apiKeyId }, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return new ApiKeyDto
        {
            Id = (Guid)row.id,
            Name = (string)row.name,
            KeyPrefix = (string)row.key_prefix,
            Scopes = (string[])row.scopes,
            RateLimit = (int)row.rate_limit,
            IsRevoked = (bool)row.is_revoked,
            CompanyId = (Guid)row.company_id,
            CreatedAt = (DateTimeOffset)row.created_at,
            RevokedAt = (DateTimeOffset?)row.revoked_at,
            ExpiresAt = (DateTimeOffset?)row.expires_at,
        };
    }
}

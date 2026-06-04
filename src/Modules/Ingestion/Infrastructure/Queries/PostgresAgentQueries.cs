namespace Liakont.Modules.Ingestion.Infrastructure.Queries;

using Dapper;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures du registre d'agents (base système), scopées par <c>tenant_id</c>. Ne sélectionne JAMAIS
/// la colonne <c>key_hash</c> : l'empreinte de clé ne quitte pas l'infrastructure (F12 §4.2).
/// </summary>
internal sealed class PostgresAgentQueries : IAgentQueries
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;

    public PostgresAgentQueries(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public async Task<IReadOnlyList<AgentSummaryDto>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, name, key_prefix, is_revoked, created_at, revoked_at, last_seen_at, last_agent_version
            FROM ingestion.agents
            WHERE tenant_id = @TenantId
            ORDER BY created_at ASC
            """;

        using var connection = await _systemConnectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken));

        var result = new List<AgentSummaryDto>();
        foreach (var row in rows)
        {
            result.Add(new AgentSummaryDto
            {
                Id = (Guid)row.id,
                Name = (string)row.name,
                KeyPrefix = (string)row.key_prefix,
                IsRevoked = (bool)row.is_revoked,
                CreatedAt = IngestionRowReader.ToDateTimeOffset((object)row.created_at),
                RevokedAt = IngestionRowReader.ToNullableDateTimeOffset((object?)row.revoked_at),
                LastSeenAtUtc = IngestionRowReader.ToNullableDateTimeOffset((object?)row.last_seen_at),
                LastAgentVersion = (string?)row.last_agent_version,
            });
        }

        return result;
    }
}

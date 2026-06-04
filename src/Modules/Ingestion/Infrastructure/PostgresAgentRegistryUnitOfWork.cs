namespace Liakont.Modules.Ingestion.Infrastructure;

using Dapper;
using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Domain.Entities;
using Npgsql;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Unité de travail Dapper du registre d'agents, ouverte sur la base SYSTÈME (partagée). Le registre
/// est cross-tenant par nécessité (il résout une clé vers son tenant avant tout contexte tenant,
/// F12 §3.1) ; les opérations de gestion sont scopées par <c>tenant_id</c> côté handler. L'historique
/// des heartbeats est append-only (aucun update/delete).
/// </summary>
internal sealed class PostgresAgentRegistryUnitOfWork : IAgentRegistryUnitOfWork
{
    private readonly TransactionScope _txn;

    private PostgresAgentRegistryUnitOfWork(TransactionScope txn)
    {
        _txn = txn;
    }

    public static async Task<PostgresAgentRegistryUnitOfWork> BeginAsync(
        ISystemConnectionFactory systemConnectionFactory,
        CancellationToken cancellationToken = default)
    {
        var txn = await TransactionScope.BeginAsync(
            new SystemConnectionFactoryAdapter(systemConnectionFactory),
            cancellationToken);
        return new PostgresAgentRegistryUnitOfWork(txn);
    }

    public async Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT {AgentRowMapper.Columns} FROM ingestion.agents WHERE id = @Id";
        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, _txn.Transaction, cancellationToken: cancellationToken));

        return row is null ? null : AgentRowMapper.Map(row);
    }

    public async Task<Agent?> GetByIdForTenantAsync(Guid id, string tenantId, CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT {AgentRowMapper.Columns} FROM ingestion.agents WHERE id = @Id AND tenant_id = @TenantId";
        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id, TenantId = tenantId }, _txn.Transaction, cancellationToken: cancellationToken));

        return row is null ? null : AgentRowMapper.Map(row);
    }

    public async Task InsertAsync(Agent agent, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO ingestion.agents
                (id, tenant_id, name, key_prefix, key_hash, is_revoked, created_at, revoked_at, last_seen_at, last_agent_version)
            VALUES
                (@Id, @TenantId, @Name, @KeyPrefix, @KeyHash, @IsRevoked, @CreatedAt, @RevokedAt, @LastSeenAtUtc, @LastAgentVersion)
            """;

        try
        {
            await _txn.Connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    agent.Id,
                    agent.TenantId,
                    agent.Name,
                    agent.KeyPrefix,
                    agent.KeyHash,
                    agent.IsRevoked,
                    agent.CreatedAt,
                    agent.RevokedAt,
                    agent.LastSeenAtUtc,
                    agent.LastAgentVersion,
                },
                _txn.Transaction,
                cancellationToken: cancellationToken));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Collision de préfixe de clé (probabilité infime) : on bloque plutôt que d'écraser.
            throw new ConflictException("Conflit de préfixe de clé d'agent — relancer l'enregistrement.", ex);
        }
    }

    public async Task UpdateAsync(Agent agent, CancellationToken cancellationToken = default)
    {
        // tenant_id est immuable (clé de routage) : jamais modifié par un update.
        const string sql = """
            UPDATE ingestion.agents
            SET name               = @Name,
                key_prefix         = @KeyPrefix,
                key_hash           = @KeyHash,
                is_revoked         = @IsRevoked,
                revoked_at         = @RevokedAt,
                last_seen_at       = @LastSeenAtUtc,
                last_agent_version = @LastAgentVersion
            WHERE id = @Id
            """;

        var rows = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                agent.Id,
                agent.Name,
                agent.KeyPrefix,
                agent.KeyHash,
                agent.IsRevoked,
                agent.RevokedAt,
                agent.LastSeenAtUtc,
                agent.LastAgentVersion,
            },
            _txn.Transaction,
            cancellationToken: cancellationToken));

        if (rows != 1)
        {
            throw new NotFoundException("Agent", agent.Id);
        }
    }

    public async Task AppendHeartbeatAsync(HeartbeatLogEntry entry, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO ingestion.agent_heartbeats
                (id, agent_id, tenant_id, contract_version, agent_version, sent_at_utc, last_successful_sync_utc, received_at_utc)
            VALUES
                (@Id, @AgentId, @TenantId, @ContractVersion, @AgentVersion, @SentAtUtc, @LastSuccessfulSyncUtc, @ReceivedAtUtc)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entry.Id,
                entry.AgentId,
                entry.TenantId,
                entry.ContractVersion,
                entry.AgentVersion,
                entry.SentAtUtc,
                entry.LastSuccessfulSyncUtc,
                entry.ReceivedAtUtc,
            },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _txn.CommitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }
}

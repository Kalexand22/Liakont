namespace Liakont.Modules.Ingestion.Application;

using Liakont.Modules.Ingestion.Domain.Entities;

/// <summary>
/// Unité de travail transactionnelle du REGISTRE D'AGENTS, ouverte sur la base SYSTÈME (partagée) :
/// le registre doit être interrogeable AVANT toute résolution de tenant, car c'est lui qui résout
/// une clé API vers son tenant (F12 §3.1). Chaque ligne porte son <c>tenant_id</c> ; les opérations
/// de gestion (révocation, rotation) sont scopées au tenant de l'opérateur (anti-fuite cross-tenant).
/// L'historique des heartbeats est append-only (aucun update/delete).
/// </summary>
public interface IAgentRegistryUnitOfWork : IAsyncDisposable
{
    /// <summary>Charge un agent par son id (chemin authentifié : l'id provient de l'identité de l'agent).</summary>
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Charge un agent par son id en le scopant à un tenant (chemin opérateur : révocation/rotation).</summary>
    Task<Agent?> GetByIdForTenantAsync(Guid id, string tenantId, CancellationToken cancellationToken = default);

    Task InsertAsync(Agent agent, CancellationToken cancellationToken = default);

    Task UpdateAsync(Agent agent, CancellationToken cancellationToken = default);

    /// <summary>Ajoute une entrée à l'historique append-only des heartbeats.</summary>
    Task AppendHeartbeatAsync(HeartbeatLogEntry entry, CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Fabrique d'unités de travail (ouvre une transaction sur la base système).</summary>
public interface IAgentRegistryUnitOfWorkFactory
{
    Task<IAgentRegistryUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}

namespace Liakont.Modules.Ingestion.Contracts.Queries;

using Liakont.Modules.Ingestion.Contracts.DTOs;

/// <summary>
/// Lectures du registre d'agents (base système). Scopées par <c>tenantId</c> — jamais de liste
/// cross-tenant (la supervision cross-tenant, en lecture seule, est un module distinct). N'expose
/// jamais l'empreinte de clé.
/// </summary>
public interface IAgentQueries
{
    Task<IReadOnlyList<AgentSummaryDto>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}

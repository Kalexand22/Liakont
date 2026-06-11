namespace Liakont.Modules.Supervision.Tests.Integration.Doubles;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;

/// <summary>
/// Double d'<see cref="IAgentQueries"/> pour les tests d'intégration de la règle « agent muet ». La vraie
/// implémentation (<c>PostgresAgentQueries</c>, base SYSTÈME) est INTERNE au module Ingestion et couverte par
/// ses propres tests ; ici on contrôle le last-seen pour prouver que la règle pilote bien le moteur + le
/// store d'alertes RÉELS (PostgreSQL) — déclenchement et auto-résolution.
/// </summary>
internal sealed class StubAgentQueries : IAgentQueries
{
    private IReadOnlyList<AgentSummaryDto> _agents;

    public StubAgentQueries(params AgentSummaryDto[] agents) => _agents = agents;

    public void Set(params AgentSummaryDto[] agents) => _agents = agents;

    public Task<IReadOnlyList<AgentSummaryDto>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_agents);
}

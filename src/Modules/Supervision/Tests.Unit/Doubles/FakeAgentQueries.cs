namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;

/// <summary>Registre d'agents fictif : retourne une liste fixe d'<see cref="AgentSummaryDto"/> par tenant.</summary>
internal sealed class FakeAgentQueries : IAgentQueries
{
    private readonly IReadOnlyList<AgentSummaryDto> _agents;

    public FakeAgentQueries(params AgentSummaryDto[] agents) => _agents = agents;

    public Task<IReadOnlyList<AgentSummaryDto>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_agents);
}

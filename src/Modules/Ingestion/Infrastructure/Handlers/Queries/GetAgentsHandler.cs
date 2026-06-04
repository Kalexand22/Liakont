namespace Liakont.Modules.Ingestion.Infrastructure.Handlers.Queries;

using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using MediatR;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>Liste les agents du tenant courant (console, supervision). Scopé au tenant du contexte.</summary>
public sealed class GetAgentsHandler : IRequestHandler<GetAgentsQuery, IReadOnlyList<AgentSummaryDto>>
{
    private readonly IAgentQueries _queries;
    private readonly ITenantContext _tenantContext;

    public GetAgentsHandler(IAgentQueries queries, ITenantContext tenantContext)
    {
        _queries = queries;
        _tenantContext = tenantContext;
    }

    public Task<IReadOnlyList<AgentSummaryDto>> Handle(GetAgentsQuery request, CancellationToken cancellationToken)
    {
        var tenantId = IngestionTenantScope.Require(_tenantContext);
        return _queries.ListByTenantAsync(tenantId, cancellationToken);
    }
}

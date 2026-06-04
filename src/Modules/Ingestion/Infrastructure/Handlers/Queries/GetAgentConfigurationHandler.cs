namespace Liakont.Modules.Ingestion.Infrastructure.Handlers.Queries;

using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Contracts.Queries;
using MediatR;

/// <summary>
/// Renvoie la configuration courante d'un agent authentifié (GET /api/agent/v1/configuration,
/// F12 §3.2). Délègue au <see cref="IAgentConfigurationProvider"/> (défaut sûr en V1).
/// </summary>
public sealed class GetAgentConfigurationHandler : IRequestHandler<GetAgentConfigurationQuery, AgentConfigurationDto>
{
    private readonly IAgentConfigurationProvider _configurationProvider;

    public GetAgentConfigurationHandler(IAgentConfigurationProvider configurationProvider)
    {
        _configurationProvider = configurationProvider;
    }

    public Task<AgentConfigurationDto> Handle(GetAgentConfigurationQuery request, CancellationToken cancellationToken) =>
        _configurationProvider.GetForTenantAsync(request.TenantId, cancellationToken);
}

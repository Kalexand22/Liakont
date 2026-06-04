namespace Liakont.Modules.Ingestion.Infrastructure.Handlers.Commands;

using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Domain.Entities;
using MediatR;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Enregistre un agent pour le tenant courant et émet sa clé API (F12 §4.2). Le tenant provient du
/// contexte (jamais du client). La clé complète n'est renvoyée qu'ici, une seule fois.
/// </summary>
public sealed class RegisterAgentHandler : IRequestHandler<RegisterAgentCommand, AgentKeyIssuedDto>
{
    private readonly IAgentRegistryUnitOfWorkFactory _uowFactory;
    private readonly ITenantContext _tenantContext;

    public RegisterAgentHandler(IAgentRegistryUnitOfWorkFactory uowFactory, ITenantContext tenantContext)
    {
        _uowFactory = uowFactory;
        _tenantContext = tenantContext;
    }

    public async Task<AgentKeyIssuedDto> Handle(RegisterAgentCommand request, CancellationToken cancellationToken)
    {
        var tenantId = IngestionTenantScope.Require(_tenantContext);

        var (agent, fullKey) = Agent.Create(tenantId, request.Name);

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            await uow.InsertAsync(agent, cancellationToken);
            await uow.CommitAsync(cancellationToken);
        }

        return new AgentKeyIssuedDto
        {
            AgentId = agent.Id,
            KeyPrefix = agent.KeyPrefix,
            FullKey = fullKey,
        };
    }
}

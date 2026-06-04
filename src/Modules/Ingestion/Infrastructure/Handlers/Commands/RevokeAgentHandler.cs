namespace Liakont.Modules.Ingestion.Infrastructure.Handlers.Commands;

using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Contracts.Commands;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Révoque un agent du tenant courant (F12 §4.2). Scopé au tenant de l'opérateur : un agent
/// appartenant à un autre tenant est introuvable (404), jamais révocable.
/// </summary>
public sealed class RevokeAgentHandler : IRequestHandler<RevokeAgentCommand>
{
    private readonly IAgentRegistryUnitOfWorkFactory _uowFactory;
    private readonly ITenantContext _tenantContext;

    public RevokeAgentHandler(IAgentRegistryUnitOfWorkFactory uowFactory, ITenantContext tenantContext)
    {
        _uowFactory = uowFactory;
        _tenantContext = tenantContext;
    }

    public async Task Handle(RevokeAgentCommand request, CancellationToken cancellationToken)
    {
        var tenantId = IngestionTenantScope.Require(_tenantContext);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        var agent = await uow.GetByIdForTenantAsync(request.AgentId, tenantId, cancellationToken)
            ?? throw new NotFoundException("Agent", request.AgentId);

        agent.Revoke();
        await uow.UpdateAsync(agent, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}

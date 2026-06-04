namespace Liakont.Modules.Ingestion.Infrastructure.Handlers.Commands;

using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Fait pivoter la clé d'un agent du tenant courant (F12 §4.2) : l'ancienne clé cesse d'être valide,
/// une nouvelle est émise (affichée une seule fois). Scopé au tenant de l'opérateur. Un agent révoqué
/// ne peut pas faire pivoter sa clé (<see cref="ConflictException"/>).
/// </summary>
public sealed class RotateAgentKeyHandler : IRequestHandler<RotateAgentKeyCommand, AgentKeyIssuedDto>
{
    private readonly IAgentRegistryUnitOfWorkFactory _uowFactory;
    private readonly ITenantContext _tenantContext;

    public RotateAgentKeyHandler(IAgentRegistryUnitOfWorkFactory uowFactory, ITenantContext tenantContext)
    {
        _uowFactory = uowFactory;
        _tenantContext = tenantContext;
    }

    public async Task<AgentKeyIssuedDto> Handle(RotateAgentKeyCommand request, CancellationToken cancellationToken)
    {
        var tenantId = IngestionTenantScope.Require(_tenantContext);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        var agent = await uow.GetByIdForTenantAsync(request.AgentId, tenantId, cancellationToken)
            ?? throw new NotFoundException("Agent", request.AgentId);

        var fullKey = agent.RotateKey();
        await uow.UpdateAsync(agent, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return new AgentKeyIssuedDto
        {
            AgentId = agent.Id,
            KeyPrefix = agent.KeyPrefix,
            FullKey = fullKey,
        };
    }
}

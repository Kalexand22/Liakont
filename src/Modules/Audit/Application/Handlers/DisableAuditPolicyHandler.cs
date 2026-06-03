namespace Stratum.Modules.Audit.Application.Handlers;

using MediatR;
using Stratum.Modules.Audit.Contracts.Commands;
using Stratum.Modules.Audit.Domain.Repositories;

public sealed class DisableAuditPolicyHandler : IRequestHandler<DisableAuditPolicyCommand>
{
    private readonly IAuditPolicyRepository _repository;

    public DisableAuditPolicyHandler(IAuditPolicyRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(DisableAuditPolicyCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByEntityType(request.EntityType, cancellationToken);

        if (existing is null)
        {
            return;
        }

        existing.Disable();
        await _repository.Update(existing, cancellationToken);
    }
}

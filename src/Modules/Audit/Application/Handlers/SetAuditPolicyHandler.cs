namespace Stratum.Modules.Audit.Application.Handlers;

using MediatR;
using Stratum.Modules.Audit.Contracts.Commands;
using Stratum.Modules.Audit.Domain.Entities;
using Stratum.Modules.Audit.Domain.Repositories;

public sealed class SetAuditPolicyHandler : IRequestHandler<SetAuditPolicyCommand>
{
    private readonly IAuditPolicyRepository _repository;

    public SetAuditPolicyHandler(IAuditPolicyRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(SetAuditPolicyCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByEntityType(request.EntityType, cancellationToken);

        if (existing is not null)
        {
            existing.Update(request.ModuleSource, request.IsEnabled, request.TrackedFields);
            await _repository.Update(existing, cancellationToken);
        }
        else
        {
            var policy = AuditPolicy.Create(
                request.EntityType,
                request.ModuleSource,
                request.IsEnabled,
                request.TrackedFields);
            await _repository.Insert(policy, cancellationToken);
        }
    }
}

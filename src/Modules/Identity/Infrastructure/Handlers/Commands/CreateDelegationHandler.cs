namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Entities;

public sealed class CreateDelegationHandler : IRequestHandler<CreateDelegationCommand, Guid>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public CreateDelegationHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task<Guid> Handle(CreateDelegationCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.DelegationCreate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.DelegationCreate);
        }

        var delegation = Delegation.Create(
            request.DelegatorId,
            request.DelegateId,
            request.Scope,
            request.ValidFrom,
            request.ValidUntil,
            request.Reason);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.InsertDelegationAsync(delegation, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return delegation.Id;
    }
}

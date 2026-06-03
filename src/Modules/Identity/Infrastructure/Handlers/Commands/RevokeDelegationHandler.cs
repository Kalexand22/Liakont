namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;

public sealed class RevokeDelegationHandler : IRequestHandler<RevokeDelegationCommand>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public RevokeDelegationHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(RevokeDelegationCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.DelegationRevoke))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.DelegationRevoke);
        }

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        var delegation = await uow.GetDelegationByIdAsync(request.DelegationId, cancellationToken)
            ?? throw new NotFoundException("Delegation", request.DelegationId);

        delegation.Revoke();
        await uow.UpdateDelegationAsync(delegation, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}

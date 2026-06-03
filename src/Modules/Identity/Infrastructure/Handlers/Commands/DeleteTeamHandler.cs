namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;

public sealed class DeleteTeamHandler : IRequestHandler<DeleteTeamCommand>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public DeleteTeamHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(DeleteTeamCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.TeamDelete))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.TeamDelete);
        }

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        var team = await uow.GetTeamByIdAsync(request.TeamId, cancellationToken)
            ?? throw new NotFoundException("Team", request.TeamId);

        await uow.DeleteTeamAsync(request.TeamId, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}

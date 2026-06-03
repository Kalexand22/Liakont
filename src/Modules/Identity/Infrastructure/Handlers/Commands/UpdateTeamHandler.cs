namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;

public sealed class UpdateTeamHandler : IRequestHandler<UpdateTeamCommand>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public UpdateTeamHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(UpdateTeamCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.TeamUpdate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.TeamUpdate);
        }

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        var team = await uow.GetTeamByIdAsync(request.TeamId, cancellationToken)
            ?? throw new NotFoundException("Team", request.TeamId);

        team.Update(request.Name, request.Description, request.ServiceCode, request.IsActive);
        await uow.UpdateTeamAsync(team, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}

namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Entities;

public sealed class CreateTeamHandler : IRequestHandler<CreateTeamCommand, Guid>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public CreateTeamHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task<Guid> Handle(CreateTeamCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.TeamCreate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.TeamCreate);
        }

        var team = Team.Create(request.Code, request.Name, request.Description, request.ServiceCode);
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.InsertTeamAsync(team, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return team.Id;
    }
}

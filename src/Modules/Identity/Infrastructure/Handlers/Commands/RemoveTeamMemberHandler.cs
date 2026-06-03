namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;

public sealed class RemoveTeamMemberHandler : IRequestHandler<RemoveTeamMemberCommand>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public RemoveTeamMemberHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(RemoveTeamMemberCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.TeamUpdate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.TeamUpdate);
        }

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.DeleteTeamMemberAsync(request.MemberId, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}

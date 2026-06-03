namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Entities;

public sealed class AddTeamMemberHandler : IRequestHandler<AddTeamMemberCommand, Guid>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public AddTeamMemberHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task<Guid> Handle(AddTeamMemberCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.TeamUpdate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.TeamUpdate);
        }

        var member = TeamMember.Create(request.TeamId, request.UserId, request.Role);
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.InsertTeamMemberAsync(member, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return member.Id;
    }
}

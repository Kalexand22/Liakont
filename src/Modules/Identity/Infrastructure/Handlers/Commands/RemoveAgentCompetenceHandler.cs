namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;

public sealed class RemoveAgentCompetenceHandler : IRequestHandler<RemoveAgentCompetenceCommand>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public RemoveAgentCompetenceHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(RemoveAgentCompetenceCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.AgentUpdate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.AgentUpdate);
        }

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.DeleteAgentCompetenceAsync(request.CompetenceId, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}

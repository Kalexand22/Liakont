namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Entities;

public sealed class AddAgentCompetenceHandler : IRequestHandler<AddAgentCompetenceCommand, Guid>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public AddAgentCompetenceHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task<Guid> Handle(AddAgentCompetenceCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.AgentUpdate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.AgentUpdate);
        }

        var competence = AgentCompetence.Create(request.UserId, request.Name, request.Category, request.ValidUntil);
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.InsertAgentCompetenceAsync(competence, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return competence.Id;
    }
}

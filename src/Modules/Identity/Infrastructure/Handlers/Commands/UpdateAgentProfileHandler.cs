namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;

public sealed class UpdateAgentProfileHandler : IRequestHandler<UpdateAgentProfileCommand>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public UpdateAgentProfileHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(UpdateAgentProfileCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.AgentUpdate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.AgentUpdate);
        }

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        var profile = await uow.GetAgentProfileByIdAsync(request.AgentProfileId, cancellationToken)
            ?? throw new NotFoundException("AgentProfile", request.AgentProfileId);

        profile.Update(
            request.ServiceCode,
            request.Title,
            request.Phone,
            request.OfficeLocation,
            request.HireDate,
            request.Notes);

        await uow.UpdateAgentProfileAsync(profile, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}

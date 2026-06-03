namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Entities;

public sealed class CreateAgentProfileHandler : IRequestHandler<CreateAgentProfileCommand, Guid>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IPermissionService _permissions;

    public CreateAgentProfileHandler(IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task<Guid> Handle(CreateAgentProfileCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.AgentCreate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.AgentCreate);
        }

        var profile = AgentProfile.Create(
            request.UserId,
            request.ServiceCode,
            request.Title,
            request.Phone,
            request.OfficeLocation,
            request.HireDate,
            request.Notes);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.InsertAgentProfileAsync(profile, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return profile.Id;
    }
}

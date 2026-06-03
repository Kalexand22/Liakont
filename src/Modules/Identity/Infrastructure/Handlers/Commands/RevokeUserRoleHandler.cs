namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Repositories;

public sealed class RevokeUserRoleHandler : IRequestHandler<RevokeUserRoleCommand>
{
    private readonly IUserRepository _userRepository;

    private readonly IRoleRepository _roleRepository;

    private readonly IIdentityUnitOfWorkFactory _uowFactory;

    private readonly IPermissionService _permissions;

    public RevokeUserRoleHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IIdentityUnitOfWorkFactory uowFactory,
        IPermissionService permissions)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(RevokeUserRoleCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.UserUpdate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.UserUpdate);
        }

        var user = await _userRepository.GetById(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{request.UserId}' not found.");

        var role = await _roleRepository.GetByName(request.RoleName, cancellationToken)
            ?? throw new InvalidOperationException($"Role '{request.RoleName}' not found.");

        user.RevokeRole(role.Id);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.UpdateUserAsync(user, cancellationToken);

        await uow.CommitAsync(cancellationToken);
    }
}

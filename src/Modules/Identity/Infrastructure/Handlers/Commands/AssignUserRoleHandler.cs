namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Contracts.Events;
using Stratum.Modules.Identity.Domain.Repositories;

public sealed class AssignUserRoleHandler : IRequestHandler<AssignUserRoleCommand>
{
    private readonly IActorContextAccessor _actorContextAccessor;

    private readonly IUserRepository _userRepository;

    private readonly IRoleRepository _roleRepository;

    private readonly IIdentityUnitOfWorkFactory _uowFactory;

    private readonly IPermissionService _permissions;

    public AssignUserRoleHandler(
        IActorContextAccessor actorContextAccessor,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IIdentityUnitOfWorkFactory uowFactory,
        IPermissionService permissions)
    {
        _actorContextAccessor = actorContextAccessor;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(AssignUserRoleCommand request, CancellationToken cancellationToken)
    {
        if (_actorContextAccessor.Current.IsAuthenticated
            && !_permissions.HasPermission(IdentityPermissions.UserUpdate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.UserUpdate);
        }

        var user = await _userRepository.GetById(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{request.UserId}' not found.");

        var role = await _roleRepository.GetByName(request.RoleName, cancellationToken)
            ?? throw new InvalidOperationException($"Role '{request.RoleName}' not found.");

        user.AssignRole(role);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.UpdateUserAsync(user, cancellationToken);

        await uow.CommitWithEventAsync(
            new IntegrationEvent<UserRoleAssignedV1>
            {
                EventId = Guid.NewGuid(),
                EventType = "identity.user_role.assigned",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = _actorContextAccessor.Current.CorrelationId,
                ModuleSource = "identity",
                Version = 1,
                Payload = new UserRoleAssignedV1
                {
                    UserId = user.Id,
                    RoleName = role.Name,
                    AssignedAt = DateTimeOffset.UtcNow,
                },
            },
            cancellationToken);
    }
}

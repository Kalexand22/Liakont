namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Contracts.Events;
using Stratum.Modules.Identity.Domain.Repositories;

public sealed class DeactivateUserHandler : IRequestHandler<DeactivateUserCommand>
{
    private readonly IActorContextAccessor _actorContextAccessor;

    private readonly IUserRepository _userRepository;

    private readonly IIdentityUnitOfWorkFactory _uowFactory;

    private readonly IPermissionService _permissions;

    public DeactivateUserHandler(IActorContextAccessor actorContextAccessor, IUserRepository userRepository, IIdentityUnitOfWorkFactory uowFactory, IPermissionService permissions)
    {
        _actorContextAccessor = actorContextAccessor;
        _userRepository = userRepository;
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        if (!_permissions.HasPermission(IdentityPermissions.UserDeactivate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.UserDeactivate);
        }

        var user = await _userRepository.GetById(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{request.UserId}' not found.");

        user.Deactivate();

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.UpdateUserAsync(user, cancellationToken);

        await uow.CommitWithEventAsync(
            new IntegrationEvent<UserDeactivatedV1>
            {
                EventId = Guid.NewGuid(),
                EventType = "identity.user.deactivated",
                OccurredAt = user.UpdatedAt!.Value,
                CorrelationId = _actorContextAccessor.Current.CorrelationId,
                ModuleSource = "identity",
                Version = 1,
                Payload = new UserDeactivatedV1
                {
                    UserId = user.Id,
                    DeactivatedAt = user.UpdatedAt!.Value,
                },
            },
            cancellationToken);
    }
}

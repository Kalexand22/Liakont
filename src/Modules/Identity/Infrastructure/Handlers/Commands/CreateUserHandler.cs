namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Contracts.Events;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Party.Contracts.Queries;

public sealed class CreateUserHandler : IRequestHandler<CreateUserCommand, Guid>
{
    private readonly IActorContextAccessor _actorContextAccessor;

    private readonly IPartyQueries _partyQueries;

    private readonly IIdentityUnitOfWorkFactory _uowFactory;

    private readonly IPermissionService _permissions;

    public CreateUserHandler(
        IActorContextAccessor actorContextAccessor,
        IPartyQueries partyQueries,
        IIdentityUnitOfWorkFactory uowFactory,
        IPermissionService permissions)
    {
        _actorContextAccessor = actorContextAccessor;
        _partyQueries = partyQueries;
        _uowFactory = uowFactory;
        _permissions = permissions;
    }

    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        if (_actorContextAccessor.Current.IsAuthenticated
            && !_permissions.HasPermission(IdentityPermissions.UserCreate))
        {
            throw new UnauthorizedAccessException("Missing permission: " + IdentityPermissions.UserCreate);
        }

        if (request.PartyId.HasValue)
        {
            var party = await _partyQueries.GetById(request.PartyId.Value, cancellationToken);
            if (party is null)
            {
                throw new InvalidOperationException($"Party '{request.PartyId.Value}' not found.");
            }
        }

        var user = User.CreateFromOidc(request.ExternalId, request.Username, request.Email, request.DisplayName);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.InsertUserAsync(user, cancellationToken);

        await uow.CommitWithEventAsync(
            new IntegrationEvent<UserCreatedV1>
            {
                EventId = Guid.NewGuid(),
                EventType = "identity.user.created",
                OccurredAt = user.CreatedAt,
                CorrelationId = _actorContextAccessor.Current.CorrelationId,
                ModuleSource = "identity",
                Version = 1,
                Payload = new UserCreatedV1
                {
                    UserId = user.Id,
                    Username = user.Username.Value,
                    Email = user.Email.Value,
                    DisplayName = request.DisplayName,
                    PartyId = user.PartyId,
                    CreatedAt = user.CreatedAt,
                },
            },
            cancellationToken);

        return user.Id;
    }
}

namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Repositories;

public sealed class LinkExternalIdHandler : IRequestHandler<LinkExternalIdCommand>
{
    private readonly IUserRepository _userRepository;

    private readonly IIdentityUnitOfWorkFactory _uowFactory;

    public LinkExternalIdHandler(IUserRepository userRepository, IIdentityUnitOfWorkFactory uowFactory)
    {
        _userRepository = userRepository;
        _uowFactory = uowFactory;
    }

    public async Task Handle(LinkExternalIdCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetById(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{request.UserId}' not found.");

        user.LinkExternalId(request.ExternalId);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.UpdateUserAsync(user, cancellationToken);

        await uow.CommitAsync(cancellationToken);
    }
}

namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Repositories;

public sealed class RevokePermissionHandler : IRequestHandler<RevokePermissionCommand>
{
    private readonly IRoleRepository _roleRepository;

    private readonly IIdentityUnitOfWorkFactory _uowFactory;

    public RevokePermissionHandler(IRoleRepository roleRepository, IIdentityUnitOfWorkFactory uowFactory)
    {
        _roleRepository = roleRepository;
        _uowFactory = uowFactory;
    }

    public async Task Handle(RevokePermissionCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByName(request.RoleName, cancellationToken)
            ?? throw new InvalidOperationException($"Role '{request.RoleName}' not found.");

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.DeleteGrantAsync(role.Id, request.Permission, cancellationToken);

        await uow.CommitAsync(cancellationToken);
    }
}

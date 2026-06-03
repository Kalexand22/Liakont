namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Domain.Repositories;

public sealed class GrantPermissionHandler : IRequestHandler<GrantPermissionCommand>
{
    private readonly IRoleRepository _roleRepository;

    private readonly IIdentityUnitOfWorkFactory _uowFactory;

    public GrantPermissionHandler(IRoleRepository roleRepository, IIdentityUnitOfWorkFactory uowFactory)
    {
        _roleRepository = roleRepository;
        _uowFactory = uowFactory;
    }

    public async Task Handle(GrantPermissionCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByName(request.RoleName, cancellationToken)
            ?? throw new InvalidOperationException($"Role '{request.RoleName}' not found.");

        var grant = Grant.Create(role.Id, request.Permission, request.ModuleSource, request.Condition);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.InsertGrantAsync(grant, cancellationToken);

        await uow.CommitAsync(cancellationToken);
    }
}

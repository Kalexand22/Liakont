namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Repositories;

public sealed class DeleteRoleHandler : IRequestHandler<DeleteRoleCommand>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IRoleRepository _roleRepo;

    public DeleteRoleHandler(IIdentityUnitOfWorkFactory uowFactory, IRoleRepository roleRepo)
    {
        _uowFactory = uowFactory;
        _roleRepo = roleRepo;
    }

    public async Task Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleRepo.GetById(request.RoleId, cancellationToken)
            ?? throw new InvalidOperationException($"Role {request.RoleId} not found.");

        role.EnsureDeletable();

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.DeleteRoleAsync(request.RoleId, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}

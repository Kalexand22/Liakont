namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Repositories;

public sealed class UpdateRoleHandler : IRequestHandler<UpdateRoleCommand>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;
    private readonly IRoleRepository _roleRepo;

    public UpdateRoleHandler(IIdentityUnitOfWorkFactory uowFactory, IRoleRepository roleRepo)
    {
        _uowFactory = uowFactory;
        _roleRepo = roleRepo;
    }

    public async Task Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleRepo.GetById(request.RoleId, cancellationToken)
            ?? throw new InvalidOperationException($"Role {request.RoleId} not found.");

        role.Rename(request.Name);
        role.UpdateDescription(request.Description);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.UpdateRoleAsync(role, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}

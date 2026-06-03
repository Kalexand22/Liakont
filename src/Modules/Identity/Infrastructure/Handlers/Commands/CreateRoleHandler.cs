namespace Stratum.Modules.Identity.Infrastructure.Handlers.Commands;

using MediatR;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Domain.Entities;

public sealed class CreateRoleHandler : IRequestHandler<CreateRoleCommand, Guid>
{
    private readonly IIdentityUnitOfWorkFactory _uowFactory;

    public CreateRoleHandler(IIdentityUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var role = Role.Create(request.Name, request.Description);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.InsertRoleAsync(role, cancellationToken);

        await uow.CommitAsync(cancellationToken);

        return role.Id;
    }
}

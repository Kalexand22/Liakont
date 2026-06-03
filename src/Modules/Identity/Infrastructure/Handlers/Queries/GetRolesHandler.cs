namespace Stratum.Modules.Identity.Infrastructure.Handlers.Queries;

using MediatR;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;

public sealed class GetRolesHandler : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IIdentityQueries _identityQueries;

    public GetRolesHandler(IIdentityQueries identityQueries)
    {
        _identityQueries = identityQueries;
    }

    public async Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        return await _identityQueries.GetRoles(cancellationToken);
    }
}

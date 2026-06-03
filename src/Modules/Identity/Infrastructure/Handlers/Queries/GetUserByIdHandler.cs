namespace Stratum.Modules.Identity.Infrastructure.Handlers.Queries;

using MediatR;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;

public sealed class GetUserByIdHandler : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    private readonly IIdentityQueries _identityQueries;

    public GetUserByIdHandler(IIdentityQueries identityQueries)
    {
        _identityQueries = identityQueries;
    }

    public async Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        return await _identityQueries.GetUserById(request.UserId, cancellationToken);
    }
}

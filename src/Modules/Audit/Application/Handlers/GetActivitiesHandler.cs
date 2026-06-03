namespace Stratum.Modules.Audit.Application.Handlers;

using MediatR;
using Stratum.Modules.Audit.Contracts.DTOs;
using Stratum.Modules.Audit.Contracts.Queries;

public sealed class GetActivitiesHandler : IRequestHandler<GetActivitiesQuery, IReadOnlyList<ActivityDto>>
{
    private readonly IAuditQueries _queries;

    public GetActivitiesHandler(IAuditQueries queries)
    {
        _queries = queries;
    }

    public async Task<IReadOnlyList<ActivityDto>> Handle(
        GetActivitiesQuery request,
        CancellationToken cancellationToken)
    {
        return await _queries.GetActivities(
            request.EntityType,
            request.EntityId,
            request.Page,
            request.PageSize,
            cancellationToken);
    }
}

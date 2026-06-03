namespace Stratum.Modules.Audit.Application.Handlers;

using MediatR;
using Stratum.Modules.Audit.Contracts.DTOs;
using Stratum.Modules.Audit.Contracts.Queries;

public sealed class GetFieldChangesHandler : IRequestHandler<GetFieldChangesQuery, IReadOnlyList<FieldChangeDto>>
{
    private readonly IAuditQueries _queries;

    public GetFieldChangesHandler(IAuditQueries queries)
    {
        _queries = queries;
    }

    public async Task<IReadOnlyList<FieldChangeDto>> Handle(
        GetFieldChangesQuery request,
        CancellationToken cancellationToken)
    {
        return await _queries.GetFieldChanges(
            request.EntityType,
            request.EntityId,
            request.Page,
            request.PageSize,
            cancellationToken);
    }
}

namespace Stratum.Modules.Audit.Application.Handlers;

using MediatR;
using Stratum.Modules.Audit.Contracts.DTOs;
using Stratum.Modules.Audit.Contracts.Queries;

public sealed class GetAuditPoliciesHandler : IRequestHandler<GetAuditPoliciesQuery, IReadOnlyList<AuditPolicyDto>>
{
    private readonly IAuditQueries _queries;

    public GetAuditPoliciesHandler(IAuditQueries queries)
    {
        _queries = queries;
    }

    public async Task<IReadOnlyList<AuditPolicyDto>> Handle(
        GetAuditPoliciesQuery request,
        CancellationToken cancellationToken)
    {
        return await _queries.GetAuditPolicies(cancellationToken);
    }
}

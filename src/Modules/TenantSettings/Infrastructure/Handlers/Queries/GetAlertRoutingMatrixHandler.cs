namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>Lit la matrice de routage des alertes du tenant courant (F12 §5.3.1, FIX212), scopée par société.</summary>
public sealed class GetAlertRoutingMatrixHandler
    : IRequestHandler<GetAlertRoutingMatrixQuery, IReadOnlyList<AlertRoutingRuleDto>>
{
    private readonly IAlertRoutingQueries _queries;
    private readonly ICompanyFilter _companyFilter;

    public GetAlertRoutingMatrixHandler(IAlertRoutingQueries queries, ICompanyFilter companyFilter)
    {
        _queries = queries;
        _companyFilter = companyFilter;
    }

    public async Task<IReadOnlyList<AlertRoutingRuleDto>> Handle(GetAlertRoutingMatrixQuery request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        return await _queries.GetAlertRoutingMatrix(companyId, cancellationToken);
    }
}

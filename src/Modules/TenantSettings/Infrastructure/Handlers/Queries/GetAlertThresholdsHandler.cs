namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

public sealed class GetAlertThresholdsHandler : IRequestHandler<GetAlertThresholdsQuery, AlertThresholdsDto?>
{
    private readonly ITenantSettingsQueries _queries;
    private readonly ICompanyFilter _companyFilter;

    public GetAlertThresholdsHandler(ITenantSettingsQueries queries, ICompanyFilter companyFilter)
    {
        _queries = queries;
        _companyFilter = companyFilter;
    }

    public async Task<AlertThresholdsDto?> Handle(GetAlertThresholdsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        return await _queries.GetAlertThresholds(companyId, cancellationToken);
    }
}

namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

public sealed class GetTenantProfileHandler : IRequestHandler<GetTenantProfileQuery, TenantProfileDto?>
{
    private readonly ITenantSettingsQueries _queries;
    private readonly ICompanyFilter _companyFilter;

    public GetTenantProfileHandler(ITenantSettingsQueries queries, ICompanyFilter companyFilter)
    {
        _queries = queries;
        _companyFilter = companyFilter;
    }

    public async Task<TenantProfileDto?> Handle(GetTenantProfileQuery request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        return await _queries.GetTenantProfile(companyId, cancellationToken);
    }
}

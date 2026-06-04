namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

public sealed class GetFiscalSettingsHandler : IRequestHandler<GetFiscalSettingsQuery, FiscalSettingsDto?>
{
    private readonly ITenantSettingsQueries _queries;
    private readonly ICompanyFilter _companyFilter;

    public GetFiscalSettingsHandler(ITenantSettingsQueries queries, ICompanyFilter companyFilter)
    {
        _queries = queries;
        _companyFilter = companyFilter;
    }

    public async Task<FiscalSettingsDto?> Handle(GetFiscalSettingsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        return await _queries.GetFiscalSettings(companyId, cancellationToken);
    }
}

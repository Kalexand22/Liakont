namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

public sealed class GetPaAccountsHandler : IRequestHandler<GetPaAccountsQuery, IReadOnlyList<PaAccountDto>>
{
    private readonly ITenantSettingsQueries _queries;
    private readonly ICompanyFilter _companyFilter;

    public GetPaAccountsHandler(ITenantSettingsQueries queries, ICompanyFilter companyFilter)
    {
        _queries = queries;
        _companyFilter = companyFilter;
    }

    public async Task<IReadOnlyList<PaAccountDto>> Handle(GetPaAccountsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        return await _queries.GetPaAccounts(companyId, cancellationToken);
    }
}

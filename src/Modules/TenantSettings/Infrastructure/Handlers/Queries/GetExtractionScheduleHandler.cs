namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

public sealed class GetExtractionScheduleHandler : IRequestHandler<GetExtractionScheduleQuery, ExtractionScheduleDto?>
{
    private readonly ITenantSettingsQueries _queries;
    private readonly ICompanyFilter _companyFilter;

    public GetExtractionScheduleHandler(ITenantSettingsQueries queries, ICompanyFilter companyFilter)
    {
        _queries = queries;
        _companyFilter = companyFilter;
    }

    public async Task<ExtractionScheduleDto?> Handle(GetExtractionScheduleQuery request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        return await _queries.GetExtractionSchedule(companyId, cancellationToken);
    }
}

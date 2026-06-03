namespace Stratum.Modules.Job.Contracts.Queries;

using Stratum.Modules.Job.Contracts.DTOs;

public interface IScheduleQueries
{
    Task<ScheduleDto?> GetByIdAsync(Guid scheduleId, CancellationToken ct = default);

    Task<IReadOnlyList<ScheduleDto>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default);
}

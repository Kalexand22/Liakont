namespace Stratum.Modules.Job.Contracts.Queries;

using Stratum.Modules.Job.Contracts.DTOs;

public interface IJobQueries
{
    Task<JobDto?> GetByIdAsync(Guid jobId, CancellationToken ct = default);

    Task<IReadOnlyList<JobDto>> ListByStatusAsync(string status, int limit = 50, CancellationToken ct = default);
}

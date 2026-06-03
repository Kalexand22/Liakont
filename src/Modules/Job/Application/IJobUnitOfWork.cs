namespace Stratum.Modules.Job.Application;

using Stratum.Common.Abstractions.Events;
using Stratum.Modules.Job.Domain.Entities;

public interface IJobUnitOfWork : IAsyncDisposable
{
    Task InsertJobAsync(JobEntry job, CancellationToken ct = default);

    Task UpdateJobAsync(JobEntry job, CancellationToken ct = default);

    Task<JobEntry?> GetJobByIdAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Acquires a single pending job using SELECT ... FOR UPDATE SKIP LOCKED.
    /// Returns null if no job is available.
    /// </summary>
    Task<JobEntry?> AcquireNextPendingJobAsync(CancellationToken ct = default);

    Task CommitWithEventAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);
}

public interface IJobUnitOfWorkFactory
{
    Task<IJobUnitOfWork> BeginAsync(CancellationToken ct = default);
}

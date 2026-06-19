namespace Liakont.Host.Startup;

using System;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts.Queries;

/// <summary>
/// Composition-root implementation of <see cref="IRecurringJobEnqueueGuard"/> (RDL08, A6-scale-2).
/// Suppresses a recurring enqueue when a job of the same type and tenant scope is already <c>Pending</c>,
/// so a fan-out that outlasts its cron interval does not stack identical triggers and starve the single-job
/// worker. Delegates the existence check to <see cref="IJobQueries.HasPendingJobOfTypeAsync"/> against the
/// system <c>job.jobs</c> table.
/// </summary>
/// <remarks>
/// Pending-only on purpose: a <c>Running</c> entry orphaned by a crash (no reaper exists, A6-scale-1) must
/// not block re-enqueue forever — the next tick re-queues a fresh entry and the idempotent
/// <see cref="ITenantJob"/> recovers. See <c>docs/adr/ADR-0006</c> §5.
/// </remarks>
internal sealed class RecurringJobEnqueueGuard : IRecurringJobEnqueueGuard
{
    private readonly IJobQueries _jobQueries;

    public RecurringJobEnqueueGuard(IJobQueries jobQueries)
    {
        ArgumentNullException.ThrowIfNull(jobQueries);
        _jobQueries = jobQueries;
    }

    public Task<bool> ShouldSuppressEnqueueAsync(
        string jobType,
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobType);
        return _jobQueries.HasPendingJobOfTypeAsync(jobType, companyId, cancellationToken);
    }
}

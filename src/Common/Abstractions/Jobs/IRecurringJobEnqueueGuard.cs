// Liakont addition (RDL08): de-duplication guard for recurring system fan-out jobs — not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Decides whether the recurring scheduler should SUPPRESS enqueuing a job because an equivalent one is
/// already queued (A6-scale-2, RDL08). A fan-out that runs longer than its cron interval (e.g. supervision
/// every 15 min) would otherwise stack identical triggers and starve the single-job worker.
/// </summary>
/// <remarks>
/// The default implementation suppresses when a <c>Pending</c> job of the same type (and tenant scope)
/// already exists — <b>not</b> when one is merely <c>Running</c>. De-duplicating against <c>Running</c>
/// would dead-lock a recurring job whose <c>Running</c> entry was orphaned by a crash (no reaper exists,
/// A6-scale-1): it would never be re-enqueued. Pending-only de-dup bounds the backlog to at most one
/// running + one queued entry, and a crashed run is recovered by the next tick re-enqueuing a fresh entry
/// (idempotent <see cref="ITenantJob"/>). See <c>docs/adr/ADR-0006</c> §5.
/// </remarks>
public interface IRecurringJobEnqueueGuard
{
    /// <summary>
    /// Returns <c>true</c> when the scheduler should SKIP enqueuing a job of <paramref name="jobType"/>
    /// for <paramref name="companyId"/> (system jobs carry <c>null</c>) because an equivalent job is
    /// already queued. Read-only; never mutates the queue.
    /// </summary>
    Task<bool> ShouldSuppressEnqueueAsync(
        string jobType,
        Guid? companyId,
        CancellationToken cancellationToken = default);
}

// Liakont addition (SOL06): multi-tenant job mechanism — not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Runs an <see cref="ITenantJob"/> once for every ACTIVE tenant, each in its own scope with the
/// connection switched to that tenant's database. A failure for one tenant is isolated (logged with
/// its tenant id and reported in the returned <see cref="TenantJobRunSummary"/>) and never stops the
/// remaining tenants. This is the single sanctioned multi-tenant fan-out mechanism
/// (<c>module-rules.md</c> §6): no module reinvents its own tenant-scanning loop.
/// </summary>
public interface ITenantJobRunner
{
    /// <summary>
    /// Executes <paramref name="job"/> for each active tenant, in order. Cancellation via
    /// <paramref name="cancellationToken"/> aborts the whole run (it is not swallowed as a per-tenant
    /// failure).
    /// </summary>
    Task<TenantJobRunSummary> RunForAllTenantsAsync(ITenantJob job, CancellationToken cancellationToken = default);
}

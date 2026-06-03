// Liakont addition (SOL06): multi-tenant job mechanism — not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.Jobs;

/// <summary>
/// A unit of work executed once per active tenant by <see cref="ITenantJobRunner"/>. The job
/// receives a <see cref="TenantJobContext"/> whose scoped services are routed to that tenant's
/// database, so it never iterates tenants nor resolves tenancy itself — fanning out over tenants
/// is the runner's single responsibility (see <c>docs/architecture/tenant-jobs.md</c> and
/// <c>module-rules.md</c> §6).
/// </summary>
public interface ITenantJob
{
    /// <summary>Stable, human-readable name used for diagnostics and logging (e.g. <c>trk.daily-anchoring</c>).</summary>
    string Name { get; }

    /// <summary>Runs the job for the single tenant carried by <paramref name="context"/>.</summary>
    Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default);
}

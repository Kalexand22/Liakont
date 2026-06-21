// Liakont addition (RDL08): per-tenant time budget for the multi-tenant job runner — not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Tuning options for <see cref="ITenantJobRunner"/>. Bound by the composition root (Host) from
/// configuration; the runner reads them via <c>IOptions&lt;TenantJobRunnerOptions&gt;</c>.
/// </summary>
public sealed class TenantJobRunnerOptions
{
    /// <summary>Configuration section name (<c>TenantJobs</c>).</summary>
    public const string SectionName = "TenantJobs";

    /// <summary>
    /// Optional time budget applied to a SINGLE tenant's <see cref="ITenantJob.ExecuteAsync"/> call
    /// (A6-scale-3, RDL08). When set, the runner wraps each tenant in a linked
    /// <see cref="System.Threading.CancellationTokenSource"/> with this timeout: a tenant that exceeds
    /// the budget becomes an ISOLATED <see cref="TenantJobFailure"/> (the remaining tenants still run),
    /// it never aborts the whole fan-out. A caller-requested cancellation is distinct and DOES abort
    /// the run (A6-runtime-4).
    /// </summary>
    /// <remarks>
    /// <b>Disabled by default (<c>null</c>)</b>. The per-tenant budget is opt-in on purpose: an overly
    /// aggressive default would turn a legitimately slow — but valid — daily WORM anchoring (TRK06) into
    /// a false failure, leaving a tenant's audit trail unsealed. Operators set the budget per deployment
    /// from observed durations. See <c>docs/adr/ADR-0006</c> §5.
    /// </remarks>
    public TimeSpan? PerTenantTimeout { get; set; }
}

// Liakont addition (SOL06): multi-tenant job mechanism — not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Outcome of running an <see cref="ITenantJob"/> across all active tenants: how many tenants were
/// targeted, how many succeeded, and the per-tenant failures. The caller (typically a system-level
/// job handler) inspects this to decide whether to retry, alert, or escalate.
/// </summary>
public sealed class TenantJobRunSummary
{
    public TenantJobRunSummary(
        string jobName,
        int totalTenants,
        int succeededCount,
        IReadOnlyList<TenantJobFailure> failures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(failures);

        JobName = jobName;
        TotalTenants = totalTenants;
        SucceededCount = succeededCount;
        Failures = failures;
    }

    /// <summary>The <see cref="ITenantJob.Name"/> that was run.</summary>
    public string JobName { get; }

    /// <summary>Number of active tenants the job was attempted for.</summary>
    public int TotalTenants { get; }

    /// <summary>Number of tenants the job completed without throwing.</summary>
    public int SucceededCount { get; }

    /// <summary>Per-tenant failures (empty when every tenant succeeded).</summary>
    public IReadOnlyList<TenantJobFailure> Failures { get; }

    /// <summary>Number of tenants for which the job threw.</summary>
    public int FailedCount => Failures.Count;

    /// <summary>
    /// True when the run found NO active tenant to fan out to. Exposed so the caller can distinguish a
    /// legitimate empty run from an anomaly (empty instance catalog from a provisioning bug or the wrong
    /// database) — the runner logs this case at <c>Warning</c> (RDL07/A6-runtime-3), not <c>Information</c>.
    /// </summary>
    public bool HadNoActiveTenants => TotalTenants == 0;

    /// <summary>
    /// True when at least one tenant failed: a partial (or total) failure of the fan-out. The job still
    /// completes (no retry/dead-letter) — escalation for high-stakes work is carried by document states and
    /// the supervision dead-man's-switch (ADR-0006 §4) — but the runner emits a structured <c>Warning</c>
    /// signal so a failed fan-out is never a silent false-green (RDL07/A6-runtime-1).
    /// </summary>
    public bool HasFailures => FailedCount > 0;
}

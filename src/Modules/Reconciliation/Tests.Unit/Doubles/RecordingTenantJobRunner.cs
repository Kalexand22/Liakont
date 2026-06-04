namespace Liakont.Modules.Reconciliation.Tests.Unit.Doubles;

using System.Threading;
using System.Threading.Tasks;
using Stratum.Common.Abstractions.Jobs;

/// <summary>Runner multi-tenant fictif : capture le job qu'on lui demande de faire tourner.</summary>
internal sealed class RecordingTenantJobRunner : ITenantJobRunner
{
    public ITenantJob? LastJob { get; private set; }

    public Task<TenantJobRunSummary> RunForAllTenantsAsync(ITenantJob job, CancellationToken cancellationToken = default)
    {
        LastJob = job;
        return Task.FromResult(new TenantJobRunSummary(job.Name, 0, 0, []));
    }
}

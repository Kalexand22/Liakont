namespace Liakont.Modules.Reconciliation.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du job SYSTÈME de réconciliation (item TRK07, F06 §7 §6) : il fait le FAN-OUT de la passe de
/// réconciliation sur tous les tenants actifs via <see cref="ITenantJobRunner"/> (SOL06). Un module ne
/// planifie pas un job par tenant : il planifie UN job système dont le handler appelle le runner
/// (tenant-jobs.md §4). Les échecs par tenant sont isolés et remontés dans le bilan du runner.
/// </summary>
public sealed class ReconciliationFanOutJobHandler : IJobHandler<ReconciliationFanOutJobPayload>
{
    private readonly ITenantJobRunner _runner;

    public ReconciliationFanOutJobHandler(ITenantJobRunner runner)
    {
        _runner = runner;
    }

    public async Task HandleAsync(ReconciliationFanOutJobPayload payload, CancellationToken ct = default)
    {
        // payload est vide (le job balaie tous les tenants) — argument conservé pour le contrat IJobHandler.
        _ = payload;
        await _runner.RunForAllTenantsAsync(new ReconciliationTenantJob(), ct);
    }
}

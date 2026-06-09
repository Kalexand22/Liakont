namespace Liakont.Modules.Supervision.Infrastructure;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du récapitulatif quotidien (digest) des alertes actives (SUP03 §3, OPTIONNEL) : fait le FAN-OUT
/// du digest sur TOUS les tenants actifs via <see cref="ITenantJobRunner"/> (SOL06), comme le dead-man's-switch
/// (<see cref="SupervisionEvaluationFanOutHandler"/>). Un module ne planifie pas un job par tenant : il planifie
/// UN job système dont le handler appelle le runner (tenant-jobs.md §4). Les échecs par tenant sont isolés par
/// le runner et JOURNALISÉS ici. Le digest n'est réellement envoyé que si l'option d'instance est activée
/// (le job tenant est un no-op sinon) — la planification peut exister sans digest actif.
/// </summary>
public sealed partial class SupervisionDigestFanOutHandler : IJobHandler<SupervisionDigestTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<SupervisionDigestFanOutHandler> _logger;

    public SupervisionDigestFanOutHandler(ITenantJobRunner runner, ILogger<SupervisionDigestFanOutHandler> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task HandleAsync(SupervisionDigestTrigger payload, CancellationToken ct = default)
    {
        TenantJobRunSummary summary = await _runner.RunForAllTenantsAsync(new SupervisionDigestTenantJob(), ct);

        if (summary.FailedCount == 0)
        {
            LogDigested(_logger, summary.SucceededCount, summary.TotalTenants);
            return;
        }

        foreach (TenantJobFailure failure in summary.Failures)
        {
            LogTenantFailure(_logger, failure.TenantId, failure.ErrorMessage);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Supervision (digest) : {Succeeded}/{Total} tenants traités.")]
    private static partial void LogDigested(ILogger logger, int succeeded, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Digest de supervision échoué pour le tenant {TenantId} : {Error}.")]
    private static partial void LogTenantFailure(ILogger logger, string tenantId, string error);
}

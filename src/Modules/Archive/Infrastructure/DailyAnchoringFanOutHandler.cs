namespace Liakont.Modules.Archive.Infrastructure;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du job SYSTÈME d'ancrage quotidien (TRK06) : fait le fan-out de
/// <see cref="DailyAnchoringTenantJob"/> sur TOUS les tenants actifs via <see cref="ITenantJobRunner"/>
/// (SOL06). Les échecs par tenant sont isolés par le runner ; ils sont journalisés ici (jamais avalés
/// silencieusement) pour alerte/supervision (SUP01). Planifié par le module Job côté Host (AddJobHandler).
/// </summary>
public sealed partial class DailyAnchoringFanOutHandler : IJobHandler<DailyAnchoringTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<DailyAnchoringFanOutHandler> _logger;

    public DailyAnchoringFanOutHandler(ITenantJobRunner runner, ILogger<DailyAnchoringFanOutHandler> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task HandleAsync(DailyAnchoringTrigger payload, CancellationToken ct = default)
    {
        TenantJobRunSummary summary = await _runner.RunForAllTenantsAsync(new DailyAnchoringTenantJob(), ct);

        if (summary.FailedCount == 0)
        {
            LogAnchored(_logger, summary.SucceededCount, summary.TotalTenants);
            return;
        }

        foreach (TenantJobFailure failure in summary.Failures)
        {
            LogTenantFailure(_logger, failure.TenantId, failure.ErrorMessage);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ancrage quotidien du coffre : {Succeeded}/{Total} tenants ancrés.")]
    private static partial void LogAnchored(ILogger logger, int succeeded, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ancrage quotidien du coffre échoué pour le tenant {TenantId} : {Error}.")]
    private static partial void LogTenantFailure(ILogger logger, string tenantId, string error);
}

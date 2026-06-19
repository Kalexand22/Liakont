namespace Liakont.Modules.SupportTrace.Infrastructure;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du job SYSTÈME de purge de la trace de support (FX06) : fait le fan-out de
/// <see cref="SupportTracePurgeTenantJob"/> sur TOUS les tenants actifs via <see cref="ITenantJobRunner"/>
/// (SOL06). Les échecs par tenant sont isolés par le runner ; ils sont journalisés ici (jamais avalés
/// silencieusement). Planifié par le module Job côté Host (AddJobHandler) ; la cadence relève du déploiement
/// (housekeeping d'une rétention courte — aucune cadence inventée).
/// </summary>
public sealed partial class SupportTracePurgeFanOutHandler : IJobHandler<SupportTracePurgeTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<SupportTracePurgeFanOutHandler> _logger;

    public SupportTracePurgeFanOutHandler(ITenantJobRunner runner, ILogger<SupportTracePurgeFanOutHandler> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task HandleAsync(SupportTracePurgeTrigger payload, CancellationToken ct = default)
    {
        TenantJobRunSummary summary = await _runner.RunForAllTenantsAsync(new SupportTracePurgeTenantJob(), ct);

        if (summary.FailedCount == 0)
        {
            LogPurged(_logger, summary.SucceededCount, summary.TotalTenants);
            return;
        }

        foreach (TenantJobFailure failure in summary.Failures)
        {
            LogTenantFailure(_logger, failure.TenantId, failure.ErrorMessage);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purge de la trace de support : {Succeeded}/{Total} tenants traités.")]
    private static partial void LogPurged(ILogger logger, int succeeded, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Purge de la trace de support échouée pour le tenant {TenantId} : {Error}.")]
    private static partial void LogTenantFailure(ILogger logger, string tenantId, string error);
}

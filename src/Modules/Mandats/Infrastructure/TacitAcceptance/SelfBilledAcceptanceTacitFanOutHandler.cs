namespace Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;

using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler du job SYSTÈME de bascule tacite des acceptations 389 (MND04, ADR-0024 §4) : fait le fan-out de
/// <see cref="SelfBilledAcceptanceTacitJob"/> sur TOUS les tenants actifs via <see cref="ITenantJobRunner"/>
/// (SOL06). Les échecs par tenant sont isolés par le runner (l'échec d'un tenant n'affecte pas les autres,
/// INV-ACCEPT-6) ; ils sont journalisés ici (jamais avalés silencieusement) pour alerte/supervision.
/// Planifié par le module Job côté Host (AddJobHandler).
/// </summary>
public sealed partial class SelfBilledAcceptanceTacitFanOutHandler : IJobHandler<SelfBilledAcceptanceTacitTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<SelfBilledAcceptanceTacitFanOutHandler> _logger;

    public SelfBilledAcceptanceTacitFanOutHandler(
        ITenantJobRunner runner, ILogger<SelfBilledAcceptanceTacitFanOutHandler> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task HandleAsync(SelfBilledAcceptanceTacitTrigger payload, CancellationToken ct = default)
    {
        TenantJobRunSummary summary = await _runner.RunForAllTenantsAsync(new SelfBilledAcceptanceTacitJob(), ct);

        if (summary.FailedCount == 0)
        {
            LogProcessed(_logger, summary.SucceededCount, summary.TotalTenants);
            return;
        }

        foreach (TenantJobFailure failure in summary.Failures)
        {
            LogTenantFailure(_logger, failure.TenantId, failure.ErrorMessage);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bascule tacite des auto-factures : {Succeeded}/{Total} tenants traités.")]
    private static partial void LogProcessed(ILogger logger, int succeeded, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bascule tacite des auto-factures échouée pour le tenant {TenantId} : {Error}.")]
    private static partial void LogTenantFailure(ILogger logger, string tenantId, string error);
}

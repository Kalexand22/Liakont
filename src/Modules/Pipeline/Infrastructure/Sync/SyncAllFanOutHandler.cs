namespace Liakont.Modules.Pipeline.Infrastructure.Sync;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler SYSTÈME du déclencheur <see cref="SyncAllTrigger"/> (PIP01d) : fait le fan-out du SYNC sur CHAQUE
/// tenant ACTIF via <see cref="ITenantJobRunner"/> (SOL06). C'est l'UNIQUE point d'orchestration multi-tenant
/// du SYNC — il n'y a AUCUNE boucle multi-tenant maison ailleurs (CLAUDE.md n°9 ; ADR-0006). L'isolation des
/// échecs (un tenant en échec n'arrête pas les autres) et la garantie « un seul job send/sync par tenant à la
/// fois » sont portées par l'ordonnanceur / le runner, pas par un verrou applicatif.
/// </summary>
public sealed partial class SyncAllFanOutHandler : IJobHandler<SyncAllTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<SyncAllFanOutHandler> _logger;

    /// <summary>Construit le handler de fan-out du SYNC.</summary>
    /// <param name="runner">Le runner multi-tenant du socle (exécute le job une fois par tenant actif).</param>
    /// <param name="logger">Le journal applicatif.</param>
    public SyncAllFanOutHandler(ITenantJobRunner runner, ILogger<SyncAllFanOutHandler> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task HandleAsync(SyncAllTrigger payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Déclenchement planifié (cron = geste opérateur).
        var job = new SyncTenantJob(PipelineRunTrigger.Scheduled);
        var summary = await _runner.RunForAllTenantsAsync(job, ct);

        if (summary.FailedCount > 0)
        {
            LogFanOutFailures(_logger, summary.JobName, summary.FailedCount, summary.TotalTenants);
        }
        else
        {
            LogFanOutCompleted(_logger, summary.JobName, summary.SucceededCount, summary.TotalTenants);
        }
    }

    [LoggerMessage(EventId = 7320, Level = LogLevel.Information,
        Message = "SYNC fan-out « {JobName} » terminé : {Succeeded}/{Total} tenant(s) traités sans échec.")]
    private static partial void LogFanOutCompleted(ILogger logger, string jobName, int succeeded, int total);

    [LoggerMessage(EventId = 7321, Level = LogLevel.Warning,
        Message = "SYNC fan-out « {JobName} » : {Failed}/{Total} tenant(s) en échec — voir les journaux par tenant.")]
    private static partial void LogFanOutFailures(ILogger logger, string jobName, int failed, int total);
}

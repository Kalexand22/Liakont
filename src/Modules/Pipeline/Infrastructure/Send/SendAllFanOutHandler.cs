namespace Liakont.Modules.Pipeline.Infrastructure.Send;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler SYSTÈME du déclencheur <see cref="SendAllTrigger"/> (PIP01c) : fait le fan-out du SEND sur CHAQUE
/// tenant ACTIF via <see cref="ITenantJobRunner"/> (SOL06). C'est l'UNIQUE point d'orchestration multi-tenant
/// du SEND — il n'y a AUCUNE boucle multi-tenant maison ailleurs (CLAUDE.md n°9 ; ADR-0006). L'isolation des
/// échecs (un tenant en échec n'arrête pas les autres) et la garantie « un seul job send/sync par tenant à la
/// fois » sont portées par l'ordonnanceur / le runner, pas par un verrou applicatif.
/// </summary>
public sealed partial class SendAllFanOutHandler : IJobHandler<SendAllTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<SendAllFanOutHandler> _logger;

    /// <summary>Construit le handler de fan-out du SEND.</summary>
    /// <param name="runner">Le runner multi-tenant du socle (exécute le job une fois par tenant actif).</param>
    /// <param name="logger">Le journal applicatif.</param>
    public SendAllFanOutHandler(ITenantJobRunner runner, ILogger<SendAllFanOutHandler> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task HandleAsync(SendAllTrigger payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Déclenchement planifié (cron = geste opérateur). Le mode simulation est porté par la charge utile.
        var job = new SendTenantJob(PipelineRunTrigger.Scheduled, payload.DryRun);
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

    [LoggerMessage(EventId = 7220, Level = LogLevel.Information,
        Message = "SEND fan-out « {JobName} » terminé : {Succeeded}/{Total} tenant(s) traités sans échec.")]
    private static partial void LogFanOutCompleted(ILogger logger, string jobName, int succeeded, int total);

    [LoggerMessage(EventId = 7221, Level = LogLevel.Warning,
        Message = "SEND fan-out « {JobName} » : {Failed}/{Total} tenant(s) en échec — voir les journaux par tenant.")]
    private static partial void LogFanOutFailures(ILogger logger, string jobName, int failed, int total);
}

namespace Liakont.Modules.Pipeline.Infrastructure.Rectification;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler SYSTÈME du déclencheur <see cref="RectifyReportsAllTrigger"/> (PIP04) : fait le fan-out de la
/// ré-évaluation des rectificatifs d'e-reporting sur CHAQUE tenant ACTIF via <see cref="ITenantJobRunner"/>
/// (SOL06). C'est l'UNIQUE point d'orchestration multi-tenant de la rectification — il n'y a AUCUNE boucle
/// multi-tenant maison ailleurs (CLAUDE.md n°9 ; ADR-0006). L'isolation des échecs (un tenant en échec
/// n'arrête pas les autres) est portée par le runner, pas par un verrou applicatif.
/// </summary>
public sealed partial class RectifyReportsAllFanOutHandler : IJobHandler<RectifyReportsAllTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<RectifyReportsAllFanOutHandler> _logger;

    /// <summary>Construit le handler de fan-out de la rectification d'e-reporting.</summary>
    /// <param name="runner">Le runner multi-tenant du socle (exécute le job une fois par tenant actif).</param>
    /// <param name="logger">Le journal applicatif.</param>
    public RectifyReportsAllFanOutHandler(ITenantJobRunner runner, ILogger<RectifyReportsAllFanOutHandler> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task HandleAsync(RectifyReportsAllTrigger payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var job = new ReportRectificationTenantJob();
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

    [LoggerMessage(EventId = 7444, Level = LogLevel.Information,
        Message = "Rectification e-reporting fan-out « {JobName} » terminée : {Succeeded}/{Total} tenant(s) traités sans échec.")]
    private static partial void LogFanOutCompleted(ILogger logger, string jobName, int succeeded, int total);

    [LoggerMessage(EventId = 7445, Level = LogLevel.Warning,
        Message = "Rectification e-reporting fan-out « {JobName} » : {Failed}/{Total} tenant(s) en échec — voir les journaux par tenant.")]
    private static partial void LogFanOutFailures(ILogger logger, string jobName, int failed, int total);
}

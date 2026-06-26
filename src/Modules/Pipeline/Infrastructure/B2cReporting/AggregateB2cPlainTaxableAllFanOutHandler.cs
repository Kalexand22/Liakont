namespace Liakont.Modules.Pipeline.Infrastructure.B2cReporting;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Handler SYSTÈME du déclencheur <see cref="AggregateB2cPlainTaxableAllTrigger"/> (#7) : fait le fan-out de
/// l'e-reporting B2C des documents ORDINAIRES taxables (facture client/TLB1, note d'honoraires/TPS1) sur CHAQUE
/// tenant ACTIF via <see cref="ITenantJobRunner"/> (SOL06). C'est l'UNIQUE point d'orchestration multi-tenant de
/// ce flux — aucune boucle multi-tenant maison ailleurs (CLAUDE.md n°9 ; ADR-0006). L'isolation des échecs (un
/// tenant en échec n'arrête pas les autres) est portée par le runner, pas par un verrou applicatif. Strictement
/// symétrique au fan-out de la marge/prix total/export.
/// </summary>
public sealed partial class AggregateB2cPlainTaxableAllFanOutHandler : IJobHandler<AggregateB2cPlainTaxableAllTrigger>
{
    private readonly ITenantJobRunner _runner;
    private readonly ILogger<AggregateB2cPlainTaxableAllFanOutHandler> _logger;

    /// <summary>Construit le handler de fan-out de l'e-reporting B2C des documents ordinaires taxables.</summary>
    /// <param name="runner">Le runner multi-tenant du socle (exécute le job une fois par tenant actif).</param>
    /// <param name="logger">Le journal applicatif.</param>
    public AggregateB2cPlainTaxableAllFanOutHandler(ITenantJobRunner runner, ILogger<AggregateB2cPlainTaxableAllFanOutHandler> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task HandleAsync(AggregateB2cPlainTaxableAllTrigger payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var job = new B2cPlainTaxableReportingTenantJob();
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

    [LoggerMessage(EventId = 7485, Level = LogLevel.Information,
        Message = "E-reporting B2C document ordinaire (TLB1/TPS1) fan-out « {JobName} » terminé : {Succeeded}/{Total} tenant(s) traités sans échec.")]
    private static partial void LogFanOutCompleted(ILogger logger, string jobName, int succeeded, int total);

    [LoggerMessage(EventId = 7486, Level = LogLevel.Warning,
        Message = "E-reporting B2C document ordinaire (TLB1/TPS1) fan-out « {JobName} » : {Failed}/{Total} tenant(s) en échec — voir les journaux par tenant.")]
    private static partial void LogFanOutFailures(ILogger logger, string jobName, int failed, int total);
}

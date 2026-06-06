namespace Liakont.Modules.Pipeline.Infrastructure.Rectification;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Ré-évaluation des rectificatifs d'e-reporting (PIP04, flux RE) — job planifié PAR TENANT (mécanique
/// <c>ITenantJob</c>/<c>TenantJobRunner</c>, SOL06 ; JAMAIS de boucle multi-tenant maison). Pour le tenant
/// courant : reprend chaque période DÉJÀ DÉCLARÉE au journal et la rectifie via
/// <see cref="ReportRectificationService"/> — un changement d'agrégat amont (avoir sur période déclarée — PIP02 ;
/// altération source détectée — TRK03) produit un rectificatif RE annule-et-remplace, IDEMPOTENT (un contenu
/// inchangé ne re-transmet pas). Une trace d'exécution est écrite (<c>pipeline.run_logs</c>). Tenant-scopé ;
/// le pipeline ne référence aucun plug-in PA concret (capacité résolue via le service).
/// </summary>
public sealed partial class ReportRectificationTenantJob : ITenantJob
{
    private readonly PipelineRunTrigger _trigger;

    /// <summary>Construit le job de rectification d'un tenant.</summary>
    /// <param name="trigger">Origine du déclenchement (planifié / manuel) — tracée dans le journal d'exécutions.</param>
    public ReportRectificationTenantJob(PipelineRunTrigger trigger = PipelineRunTrigger.Scheduled)
    {
        _trigger = trigger;
    }

    /// <inheritdoc />
    public string Name => "pipeline.rectify-reports";

    /// <inheritdoc />
    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var tenantId = context.TenantId;
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILogger<ReportRectificationTenantJob>>();
        var startedAt = timeProvider.GetUtcNow();

        var ledger = services.GetRequiredService<IReportRectificationLedger>();
        var service = services.GetRequiredService<ReportRectificationService>();

        var periods = await ledger.ListDeclaredPeriodsAsync(cancellationToken);

        var transmitted = 0;
        var pending = 0;
        var unchanged = 0;
        var failed = 0;

        foreach (var period in periods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var outcome = await service.RectifyPeriodAsync(tenantId, period.Flux, period.PeriodStart, period.PeriodEnd, cancellationToken);
                switch (outcome.Decision)
                {
                    case ReportRectificationDecision.Transmitted:
                        transmitted++;
                        break;
                    case ReportRectificationDecision.PendingCapability:
                        pending++;
                        break;
                    case ReportRectificationDecision.RejectedByPa:
                    case ReportRectificationDecision.TechnicalError:
                        failed++;
                        break;
                    default:
                        unchanged++;
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Isolation PAR PÉRIODE : une période qui lève n'avorte ni les autres ni l'écriture de la trace.
                failed++;
                LogPeriodFailed(logger, period.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), period.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ex);
            }
        }

        var detail = string.Create(
            CultureInfo.InvariantCulture,
            $"Rectification e-reporting (PIP04) : {periods.Count} période(s) déclarée(s) ré-évaluée(s) — {transmitted} rectifiée(s), {pending} en attente de capacité, {unchanged} inchangée(s), {failed} en échec.");

        var runLog = RunLog.Start(PipelineRunType.Rectify, _trigger, startedAt);
        runLog.Complete(
            completedAt: timeProvider.GetUtcNow(),
            documentsProcessed: periods.Count,
            documentsSucceeded: transmitted,
            documentsFailed: failed,
            detail: detail);
        await services.GetRequiredService<IPipelineRunLogStore>().SaveAsync(runLog, cancellationToken);

        LogCompleted(logger, tenantId, periods.Count, transmitted, pending, failed);
    }

    [LoggerMessage(EventId = 7442, Level = LogLevel.Information,
        Message = "Rectification e-reporting terminée pour le tenant « {TenantId} » : {Periods} période(s) — {Transmitted} rectifiée(s), {Pending} en attente, {Failed} en échec.")]
    private static partial void LogCompleted(ILogger logger, string tenantId, int periods, int transmitted, int pending, int failed);

    [LoggerMessage(EventId = 7443, Level = LogLevel.Warning,
        Message = "Rectification e-reporting : échec sur la période du {PeriodStart} au {PeriodEnd} — ignorée ce cycle, ré-évaluation des autres périodes poursuivie.")]
    private static partial void LogPeriodFailed(ILogger logger, string periodStart, string periodEnd, Exception exception);
}

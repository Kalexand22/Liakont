namespace Liakont.Modules.Pipeline.Infrastructure.Aggregation;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Payments.Contracts.DTOs;
using Liakont.Modules.Payments.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain;
using Liakont.Modules.Pipeline.Domain.Payments;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Agrégation de paiement (PIP03a, F09 §2) — job planifié PAR TENANT (mécanique <c>ITenantJob</c>/
/// <c>TenantJobRunner</c>, SOL06 ; JAMAIS une boucle multi-tenant maison). Pour le tenant courant : lit les
/// encaissements bruts (<see cref="IPaymentQueries"/>), les rattache à leur document et à la ventilation
/// SOURCÉE (snapshot ADR-0015, lu APRÈS la purge du staging), ventile par taux et agrège par jour×taux
/// (<see cref="PaymentAggregationCalculator"/>), puis persiste la projection (<see cref="IPaymentAggregationStore"/>)
/// avec sa qualification fiscale. AUCUN fenêtrage de période, AUCUN envoi (PIP03b). Une trace d'exécution est
/// écrite (<c>pipeline.run_logs</c>). Tenant-scopé ; le pipeline ne référence aucun plug-in PA concret
/// (capacité résolue via <see cref="IPaClientRegistry"/>).
/// </summary>
public sealed partial class PaymentAggregatorTenantJob : ITenantJob
{
    private readonly PipelineRunTrigger _trigger;

    /// <summary>Construit le job d'agrégation d'un tenant.</summary>
    /// <param name="trigger">Origine du déclenchement (planifié / manuel) — tracée dans le journal d'exécutions.</param>
    public PaymentAggregatorTenantJob(PipelineRunTrigger trigger = PipelineRunTrigger.Scheduled)
    {
        _trigger = trigger;
    }

    /// <inheritdoc />
    public string Name => "pipeline.aggregate-payments";

    /// <inheritdoc />
    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var tenantId = context.TenantId;
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILogger<PaymentAggregatorTenantJob>>();
        var startedAt = timeProvider.GetUtcNow();

        var tenantSettings = services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = await tenantSettings.GetCurrentCompanyId(cancellationToken);
        if (companyId is null)
        {
            // Profil tenant pas encore créé (CFG02) : rien à agréger (transitoire).
            await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, 0, 0, 0, "Agrégation paiement : aucun profil tenant (companyId) — rien à agréger.", cancellationToken);
            return;
        }

        var fiscal = await tenantSettings.GetFiscalSettings(companyId.Value, cancellationToken);
        var fiscalContext = BuildFiscalContext(fiscal);
        var paSupportsPaymentReporting = await ResolvePaymentReportingCapabilityAsync(services, tenantSettings, companyId.Value, tenantId, cancellationToken);

        var payments = await services.GetRequiredService<IPaymentQueries>().ListPaymentsAsync(cancellationToken);

        var resolved = new List<ResolvedPayment>();
        var ioExclusions = new List<PaymentExclusion>();
        await ResolvePaymentsAsync(services, payments, resolved, ioExclusions, cancellationToken);

        var result = PaymentAggregationCalculator.Aggregate(resolved, fiscalContext, paSupportsPaymentReporting);

        await services.GetRequiredService<IPaymentAggregationStore>().UpsertAsync(result.Aggregates, cancellationToken);

        var allExclusions = ioExclusions.Concat(result.Exclusions).ToList();
        var aggregatedPayments = resolved.Count - result.Exclusions.Count;
        var detail = Describe(payments.Count, aggregatedPayments, result.Aggregates, allExclusions);
        await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, payments.Count, aggregatedPayments, allExclusions.Count, detail, cancellationToken);
        LogAggregationCompleted(logger, tenantId, result.Aggregates.Count, aggregatedPayments, allExclusions.Count);
    }

    /// <summary>Rattache chaque encaissement à son document + ventilation (snapshot ADR-0015) ; écarte les non rattachés / sans snapshot.</summary>
    private static async Task ResolvePaymentsAsync(
        IServiceProvider services,
        IReadOnlyList<PaymentDto> payments,
        List<ResolvedPayment> resolved,
        List<PaymentExclusion> exclusions,
        CancellationToken cancellationToken)
    {
        var documents = services.GetRequiredService<IDocumentQueries>();
        var snapshots = services.GetRequiredService<IVentilationSnapshotStore>();

        foreach (var payment in payments)
        {
            if (string.IsNullOrWhiteSpace(payment.RelatedDocumentNumber))
            {
                exclusions.Add(UnattachedExclusion(payment.RelatedDocumentNumber));
                continue;
            }

            var document = await documents.GetByNumberAsync(payment.RelatedDocumentNumber, cancellationToken);
            if (document is null)
            {
                exclusions.Add(UnattachedExclusion(payment.RelatedDocumentNumber));
                continue;
            }

            // La ventilation utilisée est celle liée à l'émission du document (Document.MappingVersion,
            // happened-before ADR-0015 §4). Sans version (document non encore prêt) ou sans snapshot = écarté.
            if (string.IsNullOrWhiteSpace(document.MappingVersion))
            {
                exclusions.Add(SnapshotMissingExclusion(payment.RelatedDocumentNumber));
                continue;
            }

            var snapshot = await snapshots.GetAsync(document.Id, document.MappingVersion!, cancellationToken);
            if (snapshot is null)
            {
                exclusions.Add(SnapshotMissingExclusion(payment.RelatedDocumentNumber));
                continue;
            }

            resolved.Add(new ResolvedPayment
            {
                Date = payment.PaymentDate,
                Amount = payment.Amount,
                RelatedDocumentNumber = payment.RelatedDocumentNumber,
                Document = new DocumentVentilation
                {
                    OperationCategory = snapshot.OperationCategory,
                    Lines = snapshot.Lines,
                },
            });
        }
    }

    /// <summary>Capacité de transmission des paiements (Flux 10.4) du compte PA actif, ou <c>false</c> si aucun compte actif / plug-in indisponible.</summary>
    private static async Task<bool> ResolvePaymentReportingCapabilityAsync(
        IServiceProvider services,
        ITenantSettingsQueries tenantSettings,
        Guid companyId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var accounts = await tenantSettings.GetPaAccounts(companyId, cancellationToken);
        var active = accounts.FirstOrDefault(account => account.IsActive);
        if (active is null)
        {
            return false;
        }

        var registry = services.GetRequiredService<IPaClientRegistry>();
        var paClient = registry.Resolve(new PaAccountDescriptor(active.PluginType, tenantId));
        return paClient.Capabilities.SupportsDomesticPaymentReporting;
    }

    private static PaymentFiscalContext BuildFiscalContext(FiscalSettingsDto? fiscal) =>
        new()
        {
            VatOnDebits = fiscal?.VatOnDebits,
            HasOperationCategory = !string.IsNullOrWhiteSpace(fiscal?.OperationCategory),
            HasReportingFrequency = !string.IsNullOrWhiteSpace(fiscal?.ReportingFrequency),
            HasFeeImputationMethod = !string.IsNullOrWhiteSpace(fiscal?.FeeImputationMethod),
        };

    private static PaymentExclusion UnattachedExclusion(string? relatedDocumentNumber) =>
        new()
        {
            RelatedDocumentNumber = relatedDocumentNumber,
            Reason = PaymentExclusionReason.Unattached,
            Detail = "Règlement non rattaché à un bordereau (référence absente ou document introuvable) — " +
                     "Action opérateur : vérifiez le rapprochement de l'encaissement (Paiements).",
        };

    private static PaymentExclusion SnapshotMissingExclusion(string? relatedDocumentNumber) =>
        new()
        {
            RelatedDocumentNumber = relatedDocumentNumber,
            Reason = PaymentExclusionReason.SnapshotMissing,
            Detail = "Aucune ventilation TVA enregistrée pour la version de mapping du document — " +
                     "Action opérateur : relancez le contrôle (CHECK) du document avant l'agrégation.",
        };

    private static string Describe(
        int paymentsExamined,
        int aggregatedPayments,
        IReadOnlyList<PaymentDailyAggregate> aggregates,
        List<PaymentExclusion> exclusions)
    {
        var status = aggregates.Count > 0 ? aggregates[0].Status.ToString() : "—";
        var excludedBreakdown = exclusions
            .GroupBy(e => e.Reason)
            .Select(g => $"{g.Key}={g.Count()}")
            .ToList();
        var breakdown = excludedBreakdown.Count > 0
            ? string.Join(", ", excludedBreakdown)
            : "aucun";

        var counts = string.Create(CultureInfo.InvariantCulture, $"{aggregates.Count} agrégat(s) jour×taux (statut {status}), {aggregatedPayments}/{paymentsExamined} encaissement(s) agrégé(s), {exclusions.Count} écarté(s) ({breakdown}).");
        return "Agrégation paiement (PIP03a) : " + counts;
    }

    private static async Task WriteRunLogAsync(
        IServiceProvider services,
        TimeProvider timeProvider,
        PipelineRunTrigger trigger,
        DateTimeOffset startedAt,
        int processed,
        int succeeded,
        int excluded,
        string detail,
        CancellationToken cancellationToken)
    {
        var runLog = RunLog.Start(PipelineRunType.Aggregate, trigger, startedAt);
        runLog.Complete(
            completedAt: timeProvider.GetUtcNow(),
            documentsProcessed: processed,
            documentsSucceeded: succeeded,
            documentsFailed: excluded,
            detail: detail);
        await services.GetRequiredService<IPipelineRunLogStore>().SaveAsync(runLog, cancellationToken);
    }

    [LoggerMessage(EventId = 7400, Level = LogLevel.Information,
        Message = "Agrégation paiement terminée pour le tenant « {TenantId} » : {Aggregates} agrégat(s), {Aggregated} encaissement(s) agrégé(s), {Excluded} écarté(s).")]
    private static partial void LogAggregationCompleted(ILogger logger, string tenantId, int aggregates, int aggregated, int excluded);
}

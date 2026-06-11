namespace Liakont.Modules.Pipeline.Infrastructure;

using Liakont.Modules.Ingestion.Contracts.Events;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Infrastructure.Aggregation;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Pipeline.Infrastructure.Persistence;
using Liakont.Modules.Pipeline.Infrastructure.Queries;
using Liakont.Modules.Pipeline.Infrastructure.Rectification;
using Liakont.Modules.Pipeline.Infrastructure.Send;
using Liakont.Modules.Pipeline.Infrastructure.Sync;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Enregistrement DI du module Pipeline. PIP01a a posé les fondations (migrations DbUp — la table
/// <c>pipeline.run_logs</c> est CRÉÉE par le module, ancre MediatR, lecture du journal d'exécutions).
/// PIP01b ajoute le CHECK : le consommateur durable de <see cref="DocumentReceivedV1"/> et l'écriture du
/// journal d'exécutions. Le pipeline ne consomme les autres modules que par leurs Contracts (frontière P1).
/// </summary>
public static class PipelineModuleRegistration
{
    /// <summary>Enregistre le module Pipeline (migrations + ancre MediatR + lecture du journal d'exécutions).</summary>
    /// <param name="services">La collection de services.</param>
    /// <returns>La collection de services, pour chaînage.</returns>
    public static IServiceCollection AddPipelineModule(this IServiceCollection services)
    {
        // Ancre MediatR du module : les handlers (CHECK/SEND/SYNC) arrivent avec PIP01b+. L'enregistrement
        // est fait dès maintenant pour rester homogène avec les autres modules.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IPipelineApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(PipelineModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(PipelineModuleRegistration).Assembly));

        services.AddScoped<IPipelineRunQueries, PostgresPipelineRunQueries>();

        // API01b — lecture de la projection des agrégats jour×taux de paiement (PIP03a) pour GET /payments
        // et la page Encaissements (WEB06). La projection est écrite par PaymentAggregatorTenantJob.
        services.AddScoped<IPaymentAggregationQueries, PostgresPaymentAggregationQueries>();

        // PIP01b — CHECK : écriture du journal d'exécutions + consommateur durable de DocumentReceivedV1
        // (dispatché par l'OutboxWorker en scope SYSTÈME ; le consommateur résout un scope TENANT par slug).
        services.AddScoped<IPipelineRunLogStore, PostgresPipelineRunLogStore>();
        services.AddScoped<IIntegrationEventConsumer<DocumentReceivedV1>, DocumentReceivedConsumer>();

        // PIP03a — snapshot de ventilation TVA (ADR-0015) : écrit au CHECK, lu par l'agrégation de paiement.
        services.AddScoped<IVentilationSnapshotStore, PostgresVentilationSnapshotStore>();

        // API02b — RE-VÉRIFICATION à la demande d'un document bloqué (endpoint /documents/{id}/recheck) :
        // réutilise la source UNIQUE de la décision fiscale (DocumentCheckEvaluator). Scopé requête (le tenant
        // est résolu par la requête HTTP, ITenantContext) — IServiceProvider injecté est celui du scope courant.
        services.AddScoped<Contracts.IDocumentRecheckService, DocumentRecheckService>();

        // PIP01c — SEND : handler SYSTÈME du déclencheur SendAllTrigger (fan-out multi-tenant via
        // ITenantJobRunner, SOL06). Le SendTenantJob lui-même n'est PAS enregistré (instancié par le handler,
        // il résout ses services depuis le scope tenant — même patron que DailyAnchoringTenantJob).
        services.AddScoped<IJobHandler<SendAllTrigger>, SendAllFanOutHandler>();

        // PIP01d — SYNC : handler SYSTÈME du déclencheur SyncAllTrigger (fan-out multi-tenant, même patron que
        // le SEND). Le SyncTenantJob est instancié par le handler et résout ses services depuis le scope tenant.
        services.AddScoped<IJobHandler<SyncAllTrigger>, SyncAllFanOutHandler>();

        // PIP03a — AGRÉGATION DE PAIEMENT : projection jour×taux + handler SYSTÈME du déclencheur
        // AggregatePaymentsAllTrigger (fan-out multi-tenant, même patron que SEND/SYNC). Le job est instancié
        // par le handler et résout ses services depuis le scope tenant.
        services.AddScoped<IPaymentAggregationStore, PostgresPaymentAggregationStore>();
        services.AddScoped<IJobHandler<AggregatePaymentsAllTrigger>, AggregatePaymentsAllFanOutHandler>();

        // PIP04 — RECTIFICATIFS (flux RE annule-et-remplace) : journal append-only des rectificatifs, service
        // de rectification (build + idempotence + capacité + transmission) et handler SYSTÈME du déclencheur
        // RectifyReportsAllTrigger (fan-out multi-tenant, même patron que SEND/SYNC/AGGREGATE). Le job est
        // instancié par le handler et résout ses services depuis le scope tenant.
        services.AddScoped<IReportRectificationLedger, PostgresReportRectificationLedger>();
        services.AddScoped<ReportRectificationService>();
        services.AddScoped<IJobHandler<RectifyReportsAllTrigger>, RectifyReportsAllFanOutHandler>();

        return services;
    }
}

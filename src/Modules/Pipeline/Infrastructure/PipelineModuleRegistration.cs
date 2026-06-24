namespace Liakont.Modules.Pipeline.Infrastructure;

using Liakont.Modules.Ingestion.Contracts.Events;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Pipeline.Infrastructure.Persistence;
using Liakont.Modules.Pipeline.Infrastructure.Queries;
using Liakont.Modules.Pipeline.Infrastructure.Rectification;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;

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

        // PIP03a — AGRÉGATION DE PAIEMENT : projection jour×taux (lue par GET /payments + page Encaissements).
        services.AddScoped<IPaymentAggregationStore, PostgresPaymentAggregationStore>();

        // PIP04 — RECTIFICATIFS (flux RE annule-et-remplace) : journal append-only des rectificatifs et service
        // de rectification (build + idempotence + capacité + transmission).
        services.AddScoped<IReportRectificationLedger, PostgresReportRectificationLedger>();
        services.AddScoped<ReportRectificationService>();

        // B4 — E-REPORTING B2C MARGE (flux 10.3) : journal d'émission APPEND-ONLY portant l'anti-doublon côté
        // produit AU GRAIN DOCUMENT (attempt-once — l'API SuperPDP n'a aucune clé d'idempotence). Distinct de
        // la projection payment_aggregations (recalculée) : c'est une piste d'audit immuable des transmissions.
        services.AddScoped<IB2cMarginEmissionStore, PostgresB2cMarginEmissionStore>();

        // B4 (console) — lecture du journal d'émission marge, REGROUPÉE par lot d'émission (emission_batch_id : une
        // transmission = un POST) avec l'état courant, pour la page console des émissions de marge B2C.
        // Tenant-scopée (la connexion EST le tenant).
        services.AddScoped<IB2cMarginEmissionQueries, PostgresB2cMarginEmissionQueries>();

        // RDL06 — les 4 déclencheurs de fan-out SYSTÈME du pipeline (SendAll / SyncAll / AggregatePaymentsAll /
        // RectifyReportsAll) sont enregistrés par le HOST via AddJobHandler (AddPipelineSystemJobHandlers,
        // l'extension AddJobHandler vit dans Stratum.Modules.Job.Infrastructure, hors frontière Contracts de ce
        // module), comme l'ancrage quotidien (TRK06) ou la supervision. AddJobHandler ajoute la
        // JobHandlerRegistration singleton SANS laquelle JobHandlerResolver/JobTypeCatalog ne voient pas le
        // job → un AddScoped seul rendait ces déclencheurs ni planifiables ni dispatchables (jobs morts).
        return services;
    }
}

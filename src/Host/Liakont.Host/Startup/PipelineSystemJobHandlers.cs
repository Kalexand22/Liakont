namespace Liakont.Host.Startup;

using Liakont.Modules.Pipeline.Contracts.Jobs;
using Liakont.Modules.Pipeline.Infrastructure.Aggregation;
using Liakont.Modules.Pipeline.Infrastructure.B2cReporting;
using Liakont.Modules.Pipeline.Infrastructure.Rectification;
using Liakont.Modules.Pipeline.Infrastructure.Send;
using Liakont.Modules.Pipeline.Infrastructure.Sync;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Job.Infrastructure;

/// <summary>
/// RDL06 — câblage des 4 déclencheurs de fan-out SYSTÈME du pipeline au COMPOSITION ROOT, via
/// <c>AddJobHandler</c> (l'extension vit dans <see cref="Stratum.Modules.Job.Infrastructure"/>, que seul le
/// Host référence — le module Pipeline ne franchit pas la frontière Contracts), comme l'ancrage quotidien du
/// coffre (TRK06) ou l'évaluation de la supervision (SUP01a). <c>AddJobHandler</c> ajoute, en plus du
/// <c>AddScoped&lt;IJobHandler&lt;T&gt;&gt;</c>, la <c>JobHandlerRegistration</c> singleton lue par
/// <c>IJobHandlerResolver</c> (dispatchabilité) ET <c>IJobTypeCatalog</c> (planifiabilité). Un
/// <c>AddScoped</c> seul (état antérieur dans le module) laissait SyncAll / AggregatePaymentsAll /
/// RectifyReportsAll NI planifiables NI dispatchables — des jobs morts en production.
/// </summary>
internal static class PipelineSystemJobHandlers
{
    /// <summary>
    /// Enregistre les handlers SYSTÈME des fan-out récurrents du pipeline avec leur libellé FR (admin des jobs).
    /// Chaque handler fait le fan-out multi-tenant via <c>ITenantJobRunner</c> (SOL06) ; le job par tenant est
    /// instancié par le handler et résout ses services depuis le scope tenant (patron DailyAnchoring).
    /// </summary>
    /// <param name="services">La collection de services.</param>
    /// <returns>La collection de services, pour chaînage.</returns>
    public static IServiceCollection AddPipelineSystemJobHandlers(this IServiceCollection services)
    {
        // SEND (PIP01c) : transmission des documents prêts.
        services.AddJobHandler<SendAllTrigger, SendAllFanOutHandler>("Envoi des documents (tous les tenants)");

        // SYNC (PIP01d) : rapprochement des tax reports DGFiP + addenda WORM.
        services.AddJobHandler<SyncAllTrigger, SyncAllFanOutHandler>(
            "Synchronisation des comptes rendus (tous les tenants)");

        // AGRÉGATION DE PAIEMENT (PIP03a) : projection jour×taux des encaissements (page Encaissements).
        services.AddJobHandler<AggregatePaymentsAllTrigger, AggregatePaymentsAllFanOutHandler>(
            "Agrégation des encaissements (tous les tenants)");

        // RECTIFICATIFS (PIP04) : e-reporting annule-et-remplace.
        services.AddJobHandler<RectifyReportsAllTrigger, RectifyReportsAllFanOutHandler>(
            "Rectificatifs e-reporting (tous les tenants)");

        // E-REPORTING B2C MARGE (B4, flux 10.3) : agrégation N→1 jour×devise×taux + transmission PA (TMA1/SE).
        services.AddJobHandler<AggregateB2cMarginAllTrigger, AggregateB2cMarginAllFanOutHandler>(
            "E-reporting B2C de la marge (tous les tenants)");

        // E-REPORTING B2C PRIX TOTAL TAXABLE (BUG-8, flux 10.3) : agrégation N→1 jour×devise×taux + transmission PA (TLB1/SE).
        services.AddJobHandler<AggregateB2cTaxableAllTrigger, AggregateB2cTaxableAllFanOutHandler>(
            "E-reporting B2C au régime du prix total (tous les tenants)");

        // E-REPORTING B2C EXPORT HORS UE (BUG-11, flux 10.3) : une transaction UNITAIRE par opération + transmission PA (TLB1/SE, taux 0).
        services.AddJobHandler<AggregateB2cExportAllTrigger, AggregateB2cExportAllFanOutHandler>(
            "E-reporting B2C export hors UE (tous les tenants)");

        return services;
    }
}

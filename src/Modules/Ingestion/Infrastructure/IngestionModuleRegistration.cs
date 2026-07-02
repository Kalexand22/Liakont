namespace Liakont.Modules.Ingestion.Infrastructure;

using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Ingestion.Contracts.Authentication;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.Ingestion.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Enregistrement DI du module Ingestion (registre d'agents, authentification par clé API, heartbeat,
/// configuration d'agent). Le registre et l'historique des heartbeats vivent dans la base SYSTÈME
/// (résolution clé → tenant avant tout contexte tenant, F12 §3.1).
/// </summary>
public static class IngestionModuleRegistration
{
    public static IServiceCollection AddIngestionModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IIngestionApplicationMarker).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(IngestionModuleRegistration).Assembly);
        });

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(IngestionModuleRegistration).Assembly));

        services.AddScoped<IAgentRegistryUnitOfWorkFactory, PostgresAgentRegistryUnitOfWorkFactory>();
        services.AddScoped<IAgentQueries, PostgresAgentQueries>();
        services.AddScoped<IAgentAuthenticator, AgentAuthenticator>();
        services.AddScoped<IAgentConfigurationProvider, SafeDefaultAgentConfigurationProvider>();

        // Réception des documents (PIV04) : registre d'anti-doublon + régimes source + stockage PDF.
        services.AddScoped<IReceivedDocumentUnitOfWorkFactory, PostgresReceivedDocumentUnitOfWorkFactory>();
        services.AddScoped<ISourceTaxRegimeWriter, PostgresSourceTaxRegimeWriter>();
        services.AddScoped<ISourceTaxRegimeQueries, PostgresSourceTaxRegimeQueries>();

        // Capacités déclarées de la source (ADR-0004 D2 / RD401) : persistées par agent/tenant, lues par
        // les adaptations métier à valeur présente (RD403) et les différés tracés (RD409).
        services.AddScoped<IExtractorCapabilitiesWriter, PostgresExtractorCapabilitiesWriter>();
        services.AddScoped<IExtractorCapabilitiesQueries, PostgresExtractorCapabilitiesQueries>();
        services.AddSingleton<IIngestedPdfStore, FileSystemIngestedPdfStore>();

        // Port de création du document en état Detected : défaut sûr enregistré via TryAdd — si le module
        // Documents (TRK01) est présent, son enregistrement via Replace prime toujours, quel que soit l'ordre
        // d'enregistrement des modules. Le déclencheur durable du pipeline reste l'événement outbox.
        services.TryAddScoped<IDocumentIntake, NoOpDocumentIntake>();

        // Correspondance type d'événement → payload CLR pour le worker d'outbox (DocumentReceived, SourceAltered).
        // GDF01 : contributeur appliqué AU BUILD DI (avant le premier poll de l'OutboxWorker), en remplacement de
        // l'ancien AddHostedService<IngestionEventTypeRegistrar> qui enregistrait en concurrence avec le worker.
        services.AddSingleton<IEventTypeRegistrar, IngestionEventTypeRegistrar>();

        return services;
    }
}

namespace Liakont.Modules.Ingestion.Infrastructure;

using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Ingestion.Contracts.Authentication;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.Ingestion.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

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
        services.AddSingleton<IIngestedPdfStore, FileSystemIngestedPdfStore>();

        // Port de création du document en état Detected : no-op SÛR tant que le module Documents
        // (TRK01/TRK02) n'est pas livré ; le déclencheur durable du pipeline reste l'événement outbox.
        services.AddScoped<IDocumentIntake, NoOpDocumentIntake>();

        // Correspondance type d'événement → payload CLR pour le worker d'outbox (DocumentReceived, SourceAltered).
        services.AddHostedService<IngestionEventTypeRegistrar>();

        return services;
    }
}

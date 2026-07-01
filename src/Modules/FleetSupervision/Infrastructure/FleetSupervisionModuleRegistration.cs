namespace Liakont.Modules.FleetSupervision.Infrastructure;

using System;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module de méta-supervision de flotte (OPS04) : migrations DbUp (schéma <c>fleet</c>
/// + table des instances, base SYSTÈME), magasin du parc, lecture du dashboard, réception des heartbeats,
/// collecte de la télémétrie d'instance, publication HTTP au central et notification de mise à jour par email.
/// Les job handlers (envoi de télémétrie, notification de version) sont enregistrés par le Host (composition
/// root, via <c>AddJobHandler</c>), comme les autres jobs système (supervision, ancrage).
/// </summary>
public static class FleetSupervisionModuleRegistration
{
    /// <summary>Enregistre les services du module de méta-supervision de flotte.</summary>
    public static IServiceCollection AddFleetSupervisionModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(FleetSupervisionModuleRegistration).Assembly));

        // Horloge partagée (déterministe en test) — TryAdd : le Host la pose déjà, on ne la double pas.
        services.TryAddSingleton(TimeProvider.System);

        // Magasin du parc (base SYSTÈME) + services de lecture/écriture côté central.
        services.AddScoped<IFleetInstanceStore, PostgresFleetStore>();

        // Config d'envoi d'emails d'INSTANCE (ADR-0039), ligne singleton en base SYSTÈME. Le magasin ne
        // manipule que du ciphertext ; le chiffrement/déchiffrement reste le monopole du Host (CLAUDE.md n°6/14).
        services.AddScoped<IInstanceEmailConfigStore, PostgresInstanceEmailConfigStore>();
        services.AddScoped<IFleetHeartbeatIngestor, FleetHeartbeatIngestor>();
        services.AddScoped<IFleetQueries, FleetQueries>();

        // Côté instance (rôle reporting) : collecte de la télémétrie + publication HTTP au central.
        services.AddScoped<IInstanceTelemetryCollector, InstanceTelemetryCollector>();
        services.AddScoped<IFleetReportPublisher, HttpFleetReportPublisher>();

        // Côté central : envoi de l'email « nouvelle version disponible » via le transport email du Host.
        services.AddScoped<IFleetUpdateNotificationSender, EmailFleetUpdateNotificationSender>();

        // Clients HTTP nommés (timeouts courts : une sonde ou un envoi ne doit jamais bloquer un job).
        services.AddHttpClient(FleetHttpClients.Reporting, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient(FleetHttpClients.KeycloakProbe, client => client.Timeout = TimeSpan.FromSeconds(5));

        return services;
    }
}

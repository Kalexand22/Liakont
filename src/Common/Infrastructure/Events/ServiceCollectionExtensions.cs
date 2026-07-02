namespace Stratum.Common.Infrastructure.Events;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Outbox;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStratumEvents(this IServiceCollection services)
    {
        services.AddSingleton<IEventDispatcher, InMemoryEventDispatcher>();
        services.AddSingleton<IOutboxWriter, OutboxWriter>();

        // Liakont (GDF01) : le registre de types d'événements est peuplé À SA CONSTRUCTION en appliquant
        // tous les IEventTypeRegistrar enregistrés — donc au build DI, AVANT que le premier poll de
        // l'OutboxWorker ne puisse s'exécuter. Corrige la course de démarrage où un ...EventTypeRegistrar
        // IHostedService enregistrait ses types EN CONCURRENCE avec le worker (BackgroundService démarré
        // avant lui) : un événement pendant (ex. ged.managed-document.received) était alors vu « inconnu »
        // et marqué processed à vide, sans dead-letter (perte silencieuse). Un enregistrement tardif via
        // IHostedService reste possible (il mute le même singleton), mais tout type déclaré par un
        // contributeur est garanti connu avant le premier poll.
        services.AddSingleton<IEventTypeRegistry>(sp =>
        {
            var registry = new EventTypeRegistry();
            foreach (var registrar in sp.GetServices<IEventTypeRegistrar>())
            {
                registrar.Register(registry);
            }

            return registry;
        });

        services.AddSingleton<IDeadLetterQueries, DeadLetterQueries>();
        services.AddHostedService<OutboxWorker>();
        return services;
    }
}

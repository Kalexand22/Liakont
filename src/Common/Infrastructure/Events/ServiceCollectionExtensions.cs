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
        services.AddSingleton<IEventTypeRegistry, EventTypeRegistry>();
        services.AddSingleton<IDeadLetterQueries, DeadLetterQueries>();
        services.AddHostedService<OutboxWorker>();
        return services;
    }
}

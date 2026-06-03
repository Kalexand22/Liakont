namespace Stratum.Common.Infrastructure.Collaboration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stratum.Common.Abstractions.Collaboration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICollaborationService"/> as a singleton (shared across all circuits),
    /// <see cref="EntityChangeDispatcher"/> as the shared notifier/subscriber,
    /// and <see cref="PresenceCleanupService"/> as a hosted background service.
    /// </summary>
    public static IServiceCollection AddStratumCollaboration(this IServiceCollection services)
    {
        services.AddSingleton<ICollaborationService, CollaborationService>();

        // EntityChangeDispatcher implements both ICircuitNotifier (push) and IEntityChangeSubscriber (subscribe).
        // Register the concrete type once; resolve both interfaces from the same singleton.
        services.AddSingleton<EntityChangeDispatcher>();
        services.AddSingleton<ICircuitNotifier>(sp => sp.GetRequiredService<EntityChangeDispatcher>());
        services.AddSingleton<IEntityChangeSubscriber>(sp => sp.GetRequiredService<EntityChangeDispatcher>());

        // Background service that purges expired field focus entries every 60s.
        services.AddSingleton<IHostedService, PresenceCleanupService>();

        return services;
    }
}

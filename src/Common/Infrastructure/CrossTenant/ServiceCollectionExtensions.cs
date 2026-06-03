namespace Stratum.Common.Infrastructure.CrossTenant;

using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.CrossTenant;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCrossTenantPublisher(this IServiceCollection services)
    {
        services.AddSingleton<ICrossTenantPublisher, CrossTenantPublisher>();
        return services;
    }

    public static IServiceCollection AddCrossTenantDispatcher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CrossTenantDispatcherOptions>(
            configuration.GetSection(CrossTenantDispatcherOptions.SectionName));

        // Ensure the handler registry is available even if AddCrossTenantHandlers hasn't been called yet.
        // Uses TryAdd so that if AddCrossTenantHandlers registers the real singleton later (or was already called),
        // that registration takes priority. The factory approach resolves HandlerRegistration services lazily,
        // so ordering between AddCrossTenantDispatcher and AddCrossTenantHandlers does not matter.
        services.TryAddSingleton<ICrossTenantHandlerRegistry>(sp =>
            new CrossTenantHandlerRegistry(
                sp.GetServices<CrossTenantHandlerRegistry.HandlerRegistration>(),
                sp.GetRequiredService<ILogger<CrossTenantHandlerRegistry>>()));

        services.AddHostedService<CrossTenantDispatcher>();
        return services;
    }

    /// <summary>
    /// Scans the given assemblies for implementations of <see cref="ICrossTenantHandler{TPayload}"/>
    /// and registers them along with a <see cref="CrossTenantHandlerRegistry"/> singleton.
    /// </summary>
    public static IServiceCollection AddCrossTenantHandlers(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.IsAbstract || type.IsInterface)
                {
                    continue;
                }

                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != typeof(ICrossTenantHandler<>))
                    {
                        continue;
                    }

                    var payloadType = iface.GetGenericArguments()[0];

                    // Register the concrete handler as its interface
                    services.AddSingleton(iface, type);

                    // Register a HandlerRegistration factory that wraps the handler in a JsonElement adapter
                    var capturedIface = iface;
                    var capturedPayloadType = payloadType;
                    services.AddSingleton(sp =>
                    {
                        var handler = sp.GetRequiredService(capturedIface);
                        var adapterType = typeof(CrossTenantHandlerRegistry.JsonElementAdapter<>)
                            .MakeGenericType(capturedPayloadType);
                        var adapter = (ICrossTenantHandler<JsonElement>)Activator.CreateInstance(adapterType, handler)!;
                        return new CrossTenantHandlerRegistry.HandlerRegistration(adapter.EventType, adapter);
                    });
                }
            }
        }

        // TryAdd so that if AddCrossTenantDispatcher already registered a fallback, it doesn't conflict.
        // Both use the same factory pattern resolving HandlerRegistration services.
        services.TryAddSingleton<ICrossTenantHandlerRegistry, CrossTenantHandlerRegistry>();
        return services;
    }
}

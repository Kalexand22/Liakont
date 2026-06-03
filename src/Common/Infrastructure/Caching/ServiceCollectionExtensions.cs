namespace Stratum.Common.Infrastructure.Caching;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stratum.Common.Abstractions.Caching;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICacheService"/> with the in-memory implementation as a singleton.
    /// Idempotent: safe to call from multiple infrastructure modules that depend on caching.
    /// </summary>
    public static IServiceCollection AddStratumCache(this IServiceCollection services)
    {
        services.TryAddSingleton<ICacheService, InMemoryCacheService>();
        return services;
    }
}

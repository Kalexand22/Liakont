namespace Stratum.Common.Infrastructure.Gis;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Gis;
using Stratum.Common.Infrastructure.Caching;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers GIS services: <see cref="IGeoJsonService"/>, <see cref="ISpatialConflictDetector"/>,
    /// <see cref="IWmsClient"/>, and <see cref="IWfsClient"/>.
    /// </summary>
    public static IServiceCollection AddStratumGis(this IServiceCollection services, IConfiguration configuration)
    {
        // WMS/WFS clients depend on ICacheService for GetCapabilities caching.
        services.AddStratumCache();

        services.AddSingleton<IGeoJsonService, GeoJsonService>();
        services.AddSingleton<ISpatialConflictDetector, SpatialConflictDetector>();

        // Bind GIS options from configuration
        services.Configure<GisOptions>(configuration.GetSection(GisOptions.SectionName));

        // Register the retry handler as transient (one per HTTP client pipeline)
        services.AddTransient<RetryDelegatingHandler>();

        // WMS client with resilience handler
        services.AddHttpClient(WmsClient.HttpClientName, (sp, client) =>
        {
            var options = configuration.GetSection(GisOptions.SectionName).Get<GisOptions>() ?? new GisOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        }).AddHttpMessageHandler<RetryDelegatingHandler>();
        services.AddSingleton<IWmsClient, WmsClient>();

        // WFS client with resilience handler
        services.AddHttpClient(WfsClient.HttpClientName, (sp, client) =>
        {
            var options = configuration.GetSection(GisOptions.SectionName).Get<GisOptions>() ?? new GisOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        }).AddHttpMessageHandler<RetryDelegatingHandler>();
        services.AddSingleton<IWfsClient, WfsClient>();

        return services;
    }
}

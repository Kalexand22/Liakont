namespace Stratum.Common.Infrastructure.Portal;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Portal;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPortalQueryService(this IServiceCollection services)
    {
        services.AddTransient<IPortalQueryService, FanOutPortalQueryService>();
        return services;
    }
}

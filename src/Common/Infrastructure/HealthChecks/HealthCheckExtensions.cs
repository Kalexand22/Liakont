namespace Stratum.Common.Infrastructure.HealthChecks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering and mapping Stratum health checks.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Registers the PostgreSQL connectivity and outbox worker health checks.
    /// </summary>
    public static IServiceCollection AddStratumHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres")
            .AddCheck<OutboxWorkerHealthCheck>("outbox");

        return services;
    }

    /// <summary>
    /// Maps the <c>/health</c> endpoint. Returns 200 Healthy, 200 Degraded, or 503 Unhealthy.
    /// </summary>
    public static IEndpointRouteBuilder MapStratumHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health").AllowAnonymous();
        return endpoints;
    }
}

// Liakont addition (SOL06): multi-tenant job mechanism — not part of the original Stratum vendoring.
namespace Stratum.Common.Infrastructure.Jobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stratum.Common.Abstractions.Jobs;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the multi-tenant job runner (<see cref="ITenantJobRunner"/>).
    /// </summary>
    /// <remarks>
    /// The composition root (Host) MUST also register an
    /// <see cref="Stratum.Common.Abstractions.MultiTenancy.ITenantScopeFactory"/> implementation —
    /// it is the only layer permitted to establish (mutate) the ambient tenant context. Without it,
    /// resolving <see cref="ITenantJobRunner"/> fails fast at first use.
    /// </remarks>
    public static IServiceCollection AddTenantJobs(this IServiceCollection services)
    {
        services.TryAddTransient<ITenantJobRunner, TenantJobRunner>();
        return services;
    }
}

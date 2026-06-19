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
    /// <param name="configureOptions">
    /// Optional tuning of <see cref="TenantJobRunnerOptions"/> (e.g. a per-tenant time budget, RDL08).
    /// When omitted the budget stays disabled (<c>null</c>) — unchanged behaviour.
    /// </param>
    public static IServiceCollection AddTenantJobs(
        this IServiceCollection services,
        Action<TenantJobRunnerOptions>? configureOptions = null)
    {
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        // AddOptions makes IOptions<TenantJobRunnerOptions> resolvable for the runner's constructor even
        // when nothing is configured (PerTenantTimeout stays null = the per-tenant budget is disabled).
        services.AddOptions<TenantJobRunnerOptions>();
        services.TryAddTransient<ITenantJobRunner, TenantJobRunner>();
        return services;
    }
}

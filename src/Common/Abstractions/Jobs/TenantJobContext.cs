// Liakont addition (SOL06): multi-tenant job mechanism — not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Execution context handed to an <see cref="ITenantJob"/> for ONE tenant. <see cref="Services"/>
/// is the tenant-scoped service provider: services resolved from it (module repositories, the
/// connection factory) are already routed to <see cref="TenantId"/>'s database, so the job writes
/// its normal tenant-scoped queries without ever touching tenant resolution itself.
/// </summary>
public sealed class TenantJobContext
{
    public TenantJobContext(string tenantId, IServiceProvider services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(services);

        TenantId = tenantId;
        Services = services;
    }

    /// <summary>The tenant this invocation runs against.</summary>
    public string TenantId { get; }

    /// <summary>The tenant-scoped service provider (its connection factory targets this tenant's database).</summary>
    public IServiceProvider Services { get; }
}

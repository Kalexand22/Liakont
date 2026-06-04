// Liakont addition (SOL06): multi-tenant job mechanism — not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// A dependency-injection scope with a single tenant established as the ambient
/// <see cref="ITenantContext"/>. Tenant-scoped services resolved from <see cref="Services"/>
/// (notably the connection factory and module repositories) are already routed to that tenant's
/// database. Disposing the scope releases the tenant context and the scoped services.
/// </summary>
public interface ITenantScope : IAsyncDisposable
{
    /// <summary>The tenant this scope is bound to.</summary>
    string TenantId { get; }

    /// <summary>The tenant-scoped service provider.</summary>
    IServiceProvider Services { get; }
}

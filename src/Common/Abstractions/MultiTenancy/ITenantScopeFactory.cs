// Liakont addition (SOL06): multi-tenant job mechanism — not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Creates a dependency-injection scope with a specific tenant established as the ambient
/// <see cref="ITenantContext"/>, so background work (multi-tenant jobs) can run unit-of-work logic
/// against a chosen tenant's database — the same routing the request pipeline gives an HTTP scope.
/// </summary>
/// <remarks>
/// The implementation lives in the composition root (Host): it is the only layer permitted to
/// mutate the tenant context (see the mutable tenant context, kept internal to the Host on purpose).
/// Domain, application and infrastructure layers establish a tenant only indirectly, by handing an
/// <see cref="Stratum.Common.Abstractions.Jobs.ITenantJob"/> to
/// <see cref="Stratum.Common.Abstractions.Jobs.ITenantJobRunner"/>.
/// </remarks>
public interface ITenantScopeFactory
{
    /// <summary>
    /// Creates a scope bound to <paramref name="tenantId"/>. The caller owns the returned scope and
    /// must dispose it (preferably with <c>await using</c>).
    /// </summary>
    ITenantScope Create(string tenantId);
}

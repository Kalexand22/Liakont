namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Resolves the current tenant from the ambient request context.
/// Implementations form a chain evaluated in priority order (subdomain > header > JWT claim).
/// Defined in Common.Abstractions for visibility; implemented in Host (HTTP-specific).
/// </summary>
/// <remarks>
/// Implementations inject <c>IHttpContextAccessor</c> to access the current HTTP request,
/// keeping this interface free of ASP.NET dependencies.
/// </remarks>
public interface ITenantResolver
{
    /// <summary>
    /// Attempts to resolve a tenant identifier.
    /// Returns <c>null</c> if this resolver cannot determine the tenant.
    /// </summary>
    string? Resolve();
}

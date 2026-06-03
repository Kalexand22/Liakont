namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Provides the resolved tenant for the current HTTP request.
/// Registered as Scoped — one instance per request.
/// Set by <c>TenantMiddleware</c>, consumed by repositories, MediatR behaviors, and filters.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The resolved tenant identifier, or <c>null</c> if no tenant was resolved
    /// (e.g., unauthenticated requests, system-level endpoints).
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// <c>true</c> if a tenant was successfully resolved for this request.
    /// </summary>
    bool IsResolved { get; }
}

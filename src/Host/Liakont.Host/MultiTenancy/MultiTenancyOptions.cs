namespace Liakont.Host.MultiTenancy;

/// <summary>
/// Configuration options for multi-tenant behavior.
/// Bound from <c>MultiTenancy</c> configuration section.
/// </summary>
public sealed class MultiTenancyOptions
{
    public const string SectionName = "MultiTenancy";

    /// <summary>
    /// When <c>true</c>, API endpoints that require a tenant will return 400 Bad Request
    /// if no tenant is resolved. When <c>false</c> (default), the middleware resolves the
    /// tenant if available but does not enforce it — requests proceed without a tenant.
    /// Turn this on once the full multi-tenant stack (MT01–MT06) is deployed.
    /// </summary>
    public bool EnforceOnApiEndpoints { get; set; }
}

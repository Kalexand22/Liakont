namespace Liakont.Host.MultiTenancy;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Evaluates a chain of <see cref="ITenantResolver"/> implementations in priority order.
/// Returns the first non-null result. Order: subdomain > header > JWT claim.
/// </summary>
internal sealed class CompositeTenantResolver
{
    private readonly IEnumerable<ITenantResolver> _resolvers;

    public CompositeTenantResolver(IEnumerable<ITenantResolver> resolvers)
    {
        _resolvers = resolvers;
    }

    /// <summary>
    /// Evaluates each resolver in registration order and returns the first resolved tenant ID.
    /// </summary>
    public string? Resolve()
    {
        foreach (var resolver in _resolvers)
        {
            var tenantId = resolver.Resolve();
            if (tenantId is not null)
            {
                return tenantId;
            }
        }

        return null;
    }
}

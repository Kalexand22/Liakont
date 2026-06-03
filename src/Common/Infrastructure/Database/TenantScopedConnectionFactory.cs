namespace Stratum.Common.Infrastructure.Database;

using System.Data;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Scoped <see cref="IConnectionFactory"/> that routes connections based on the
/// current <see cref="ITenantContext"/>. When a tenant is resolved, opens a
/// connection to the tenant's database. When no tenant is resolved (system context),
/// falls back to the system database.
/// </summary>
public sealed class TenantScopedConnectionFactory : IConnectionFactory
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantConnectionFactory _tenantConnectionFactory;

    public TenantScopedConnectionFactory(
        ITenantContext tenantContext,
        ITenantConnectionFactory tenantConnectionFactory)
    {
        _tenantContext = tenantContext;
        _tenantConnectionFactory = tenantConnectionFactory;
    }

    public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        return _tenantConnectionFactory.OpenAsync(_tenantContext.TenantId, cancellationToken);
    }
}

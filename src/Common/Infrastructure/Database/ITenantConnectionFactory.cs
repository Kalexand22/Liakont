namespace Stratum.Common.Infrastructure.Database;

using System.Data;

/// <summary>
/// Opens database connections scoped to a specific tenant.
/// For schema-per-tenant, sets <c>search_path</c> to the tenant's schema.
/// For DB-per-tenant (future), routes to a tenant-specific connection string.
/// Tenant "system" (null) uses the default connection with default search_path.
/// </summary>
public interface ITenantConnectionFactory
{
    Task<IDbConnection> OpenAsync(string? tenantId, CancellationToken cancellationToken = default);
}

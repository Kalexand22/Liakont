namespace Stratum.Common.Infrastructure.Database;

/// <summary>
/// Configuration for tenant-aware database connections.
/// Bound from <c>appsettings.json</c> section <c>"TenantConnections"</c>.
/// </summary>
public sealed class TenantConnectionOptions
{
    public const string SectionName = "TenantConnections";

    /// <summary>
    /// Database name prefix for database-per-tenant isolation.
    /// Tenant database = <c>{DatabasePrefix}{tenantId}</c> (e.g., <c>stratum_acme</c>).
    /// Default: <c>"stratum_"</c>.
    /// </summary>
    public string DatabasePrefix { get; init; } = "stratum_";

    /// <summary>
    /// Per-tenant connection string overrides. When a tenant has an entry here,
    /// the factory uses this connection string instead of deriving one from the
    /// default connection string + <see cref="DatabasePrefix"/>.
    /// Key: tenant ID. Value: full PostgreSQL connection string.
    /// </summary>
    public Dictionary<string, string> ConnectionStrings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

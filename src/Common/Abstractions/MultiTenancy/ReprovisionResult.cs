namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Result of a tenant reprovisioning operation (re-running pending migrations
/// on an existing tenant database).
/// </summary>
public sealed class ReprovisionResult
{
    private ReprovisionResult()
    {
    }

    /// <summary>
    /// <c>true</c> if the reprovisioning completed successfully.
    /// </summary>
    public bool Success { get; private init; }

    /// <summary>
    /// <c>true</c> if the tenant was not found in the registry.
    /// Allows callers to distinguish "not found" from other failures.
    /// </summary>
    public bool TenantNotFound { get; private init; }

    /// <summary>
    /// <c>true</c> if the tenant exists but is deactivated.
    /// Allows callers to return 422 (business rule violation).
    /// </summary>
    public bool TenantDeactivated { get; private init; }

    /// <summary>
    /// The database name that was reprovisioned.
    /// </summary>
    public string? DatabaseName { get; private init; }

    /// <summary>
    /// Number of migration scripts that were applied during reprovisioning.
    /// Zero means the database was already up to date.
    /// </summary>
    public int MigrationsApplied { get; private init; }

    /// <summary>
    /// Error message if reprovisioning failed. <c>null</c> on success.
    /// </summary>
    public string? ErrorMessage { get; private init; }

    public static ReprovisionResult Completed(string databaseName, int migrationsApplied) =>
        new() { Success = true, DatabaseName = databaseName, MigrationsApplied = migrationsApplied };

    public static ReprovisionResult NotFound(string tenantId) =>
        new() { Success = false, TenantNotFound = true, ErrorMessage = $"Tenant '{tenantId}' not found in registry." };

    public static ReprovisionResult Deactivated(string tenantId) =>
        new() { Success = false, TenantDeactivated = true, ErrorMessage = $"Tenant '{tenantId}' is deactivated and cannot be reprovisioned." };

    public static ReprovisionResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}

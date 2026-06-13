namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Result of a tenant provisioning operation.
/// </summary>
public sealed class TenantProvisionResult
{
    private TenantProvisionResult()
    {
    }

    /// <summary>
    /// <c>true</c> if the provisioning completed successfully (or was already done).
    /// </summary>
    public bool Success { get; private init; }

    /// <summary>
    /// <c>true</c> if the tenant was already provisioned before this call (idempotent success).
    /// </summary>
    public bool AlreadyProvisioned { get; private init; }

    /// <summary>
    /// The database name created for the tenant (e.g., <c>stratum_acme</c>).
    /// </summary>
    public string? DatabaseName { get; private init; }

    /// <summary>
    /// The Keycloak realm name (e.g., <c>stratum-acme</c>).
    /// </summary>
    public string? RealmName { get; private init; }

    /// <summary>
    /// The full Keycloak authority/issuer URL (e.g., <c>http://localhost:8080/realms/stratum-acme</c>).
    /// </summary>
    public string? Authority { get; private init; }

    /// <summary>
    /// Error message if provisioning failed. <c>null</c> on success.
    /// </summary>
    public string? ErrorMessage { get; private init; }

    public static TenantProvisionResult Created(
        string databaseName, string realmName, string authority) =>
        new()
        {
            Success = true,
            DatabaseName = databaseName,
            RealmName = realmName,
            Authority = authority,
        };

    public static TenantProvisionResult Idempotent(string databaseName, string realmName, string authority) =>
        new() { Success = true, AlreadyProvisioned = true, DatabaseName = databaseName, RealmName = realmName, Authority = authority };

    public static TenantProvisionResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}

namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Provisions a new tenant: creates schema, runs migrations, seeds base data.
/// Idempotent — calling for an already-provisioned tenant returns success with
/// <see cref="TenantProvisionResult.AlreadyProvisioned"/> set to <c>true</c>.
/// </summary>
public interface ITenantProvisioningService
{
    Task<TenantProvisionResult> ProvisionAsync(
        TenantProvisionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies any missing migrations to all active tenant databases.
    /// Called at startup to ensure existing tenants stay up-to-date
    /// when new migrations are added.  Idempotent — DbUp skips
    /// migrations already recorded in each tenant's <c>outbox.schema_versions</c>.
    /// Inaccessible tenant databases are skipped with a warning.
    /// </summary>
    Task MigrateExistingTenantsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs pending migrations on an existing tenant's database.
    /// This is useful when new migration scripts have been deployed and
    /// existing tenant databases need to be brought up to date.
    /// </summary>
    Task<ReprovisionResult> ReprovisionAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a tenant: deletes its Keycloak realm, unregisters
    /// from the in-memory realm registry, and soft-deletes in the system DB.
    /// </summary>
    Task<DeactivationResult> DeactivateAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}

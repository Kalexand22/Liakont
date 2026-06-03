namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Result of a tenant deactivation operation (soft-delete + Keycloak realm cleanup).
/// </summary>
public sealed class DeactivationResult
{
    private DeactivationResult()
    {
    }

    /// <summary>
    /// <c>true</c> if the deactivation completed successfully.
    /// </summary>
    public bool Success { get; private init; }

    /// <summary>
    /// <c>true</c> if the tenant was not found in the registry.
    /// </summary>
    public bool TenantNotFound { get; private init; }

    /// <summary>
    /// <c>true</c> if the tenant was already deactivated.
    /// </summary>
    public bool AlreadyDeactivated { get; private init; }

    /// <summary>
    /// Error message if deactivation failed. <c>null</c> on success.
    /// </summary>
    public string? ErrorMessage { get; private init; }

    public static DeactivationResult Completed() =>
        new() { Success = true };

    public static DeactivationResult NotFound(string tenantId) =>
        new() { Success = false, TenantNotFound = true, ErrorMessage = $"Tenant '{tenantId}' not found." };

    public static DeactivationResult AlreadyInactive(string tenantId) =>
        new() { Success = true, AlreadyDeactivated = true };

    public static DeactivationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}

namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Request to provision a new tenant.
/// </summary>
public sealed class TenantProvisionRequest
{
    /// <summary>
    /// Unique tenant identifier. Must match the tenant ID format:
    /// 1-63 lowercase alphanumeric characters or hyphens, starting and ending with a letter or digit.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Human-readable display name for the tenant (e.g., "Acme Corp").
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Email address for the tenant's initial administrator account.
    /// </summary>
    public required string AdminEmail { get; init; }
}

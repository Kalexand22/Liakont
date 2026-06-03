namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Data transfer object representing a tenant in the admin API.
/// </summary>
public sealed class TenantDto
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string AdminEmail { get; init; }

    public required string DatabaseName { get; init; }

    public string? RealmName { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset ProvisionedAt { get; init; }
}

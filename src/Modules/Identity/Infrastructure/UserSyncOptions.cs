namespace Stratum.Modules.Identity.Infrastructure;

/// <summary>
/// Configuration for OIDC user auto-provisioning.
/// Bound from <c>appsettings.json</c> section <c>"Identity:UserSync"</c>.
/// </summary>
public sealed class UserSyncOptions
{
    public const string SectionName = "Identity:UserSync";

    /// <summary>
    /// Name of the role automatically assigned to newly provisioned OIDC users.
    /// Default: <c>"User"</c>.
    /// </summary>
    public string DefaultRoleName { get; init; } = "User";
}

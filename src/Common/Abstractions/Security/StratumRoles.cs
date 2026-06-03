namespace Stratum.Common.Abstractions.Security;

/// <summary>
/// Canonical names for Keycloak realm roles provisioned by Stratum.
/// Shared between realm provisioner, authorization handlers, and policies.
/// </summary>
public static class StratumRoles
{
    public const string User = "stratum-user";
    public const string Admin = "stratum-admin";
    public const string Volunteer = "stratum-volunteer";
    public const string SystemAdmin = "SystemAdmin";

    /// <summary>
    /// Roles that grant full (non-restricted) access. A user holding any of these
    /// roles is not subject to volunteer restrictions even if they also hold
    /// <see cref="Volunteer"/>.
    /// </summary>
    public static readonly IReadOnlySet<string> PrivilegedRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { User, Admin, SystemAdmin };
}

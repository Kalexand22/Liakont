namespace Liakont.Host.Security;

using System.Security.Claims;

/// <summary>
/// Roles granted unconditional access to every permission-gated UI element
/// and API endpoint. A user holding any of these roles bypasses both
/// <see cref="ClaimsPermissionService"/> and <see cref="PermissionAuthorizationHandler"/>.
/// </summary>
internal static class SuperAdminRoles
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin",          // legacy/dev cookie-auth admin role (seeded by AdminUserSeeder)
        "SystemAdmin",    // Stratum.Common.Abstractions.Security.StratumRoles.SystemAdmin
        "stratum-admin",  // Keycloak realm admin role
    };

    public static bool IsSuperAdmin(ClaimsPrincipal user)
    {
        // Check via IsInRole first (handles whatever RoleClaimType the identity declares).
        foreach (var role in Names)
        {
            if (user.IsInRole(role))
            {
                return true;
            }
        }

        // Fallback: scan all common role claim types directly. OIDC cookies often declare
        // RoleClaimType="roles" but persist roles under ClaimTypes.Role URI, so IsInRole
        // misses them. Be permissive — multiple realms / providers, multiple shapes.
        foreach (var claim in user.Claims)
        {
            if ((claim.Type == ClaimTypes.Role
                 || claim.Type == "role"
                 || claim.Type == "roles")
                && Names.Contains(claim.Value))
            {
                return true;
            }
        }

        return false;
    }
}

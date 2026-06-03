namespace Liakont.Host.Security;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Restricts users with only the stratum-volunteer role to volunteer-permitted permissions.
/// When a permission check occurs:
/// <list type="bullet">
/// <item>If the user is NOT a volunteer, this handler does nothing (defers to PermissionAuthorizationHandler).</item>
/// <item>If the user IS a volunteer AND has a privileged role (admin/user), defers (higher role prevails).</item>
/// <item>If the user IS a volunteer-only, succeeds for allowed permissions, explicitly fails for others.</item>
/// </list>
/// </summary>
internal sealed class VolunteerAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!IsVolunteerOnly(context.User))
        {
            return Task.CompletedTask;
        }

        if (VolunteerPermissions.IsAllowed(requirement.Permission))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, $"Volunteer role does not permit '{requirement.Permission}'"));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns true if the user holds the volunteer role but none of the privileged roles.
    /// Checks both "roles" (Keycloak JWT claim) and ClaimTypes.Role (defensive, in case
    /// middleware or identity providers populate the standard claim type).
    /// </summary>
    private static bool IsVolunteerOnly(ClaimsPrincipal user)
    {
        var hasVolunteer = false;

        foreach (var claim in user.Claims)
        {
            if (claim.Type is not ("roles" or ClaimTypes.Role))
            {
                continue;
            }

            if (StratumRoles.PrivilegedRoles.Contains(claim.Value))
            {
                return false;
            }

            if (string.Equals(claim.Value, StratumRoles.Volunteer, StringComparison.OrdinalIgnoreCase))
            {
                hasVolunteer = true;
            }
        }

        return hasVolunteer;
    }
}

namespace Liakont.Host.Security;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Stratum.Modules.Identity.Contracts.Queries;

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IIdentityQueries _identityQueries;

    public PermissionAuthorizationHandler(IIdentityQueries identityQueries)
    {
        _identityQueries = identityQueries;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (SuperAdminRoles.IsSuperAdmin(context.User))
        {
            context.Succeed(requirement);
            return;
        }

        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdStr, out var userId) || userId == Guid.Empty)
        {
            return;
        }

        var hasPermission = await _identityQueries.UserHasPermission(userId, requirement.Permission);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}

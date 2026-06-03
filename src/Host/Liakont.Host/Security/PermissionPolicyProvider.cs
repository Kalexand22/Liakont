namespace Liakont.Host.Security;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

/// <summary>
/// Dynamically creates authorization policies for permission strings.
/// Any policy name not pre-registered is treated as a permission requirement,
/// allowing endpoints to call RequireAuthorization("module.action") without
/// upfront registration of every permission in Program.cs.
/// </summary>
internal sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
    }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var existing = await base.GetPolicyAsync(policyName);

        if (existing is not null)
        {
            return existing;
        }

        return new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}

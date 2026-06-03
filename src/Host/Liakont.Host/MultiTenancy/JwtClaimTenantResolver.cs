namespace Liakont.Host.MultiTenancy;

using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Resolves tenant from the <c>tenant_id</c> JWT claim.
/// Fallback resolver when subdomain and header are not available.
/// </summary>
internal sealed class JwtClaimTenantResolver : ITenantResolver
{
    internal const string ClaimType = "tenant_id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtClaimTenantResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? Resolve()
    {
        var value = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimType)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

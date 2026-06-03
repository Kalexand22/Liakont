namespace Liakont.Host.MultiTenancy;

using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Resolves tenant from the <c>X-Tenant-Id</c> HTTP header.
/// Useful for API clients that cannot use subdomain-based routing.
/// </summary>
internal sealed class HeaderTenantResolver : ITenantResolver
{
    internal const string HeaderName = "X-Tenant-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HeaderTenantResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? Resolve()
    {
        var value = _httpContextAccessor.HttpContext?.Request.Headers[HeaderName].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

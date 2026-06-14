namespace Liakont.Host.MultiTenancy;

using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Resolves tenant from the <c>X-Tenant-Id</c> HTTP header.
/// Useful for API clients that cannot use subdomain-based routing.
/// <para>
/// Voie CLIENT-FOURNIE (<see cref="IClientSuppliedTenantResolver"/>) : non autoritaire en realm unique
/// (ADR-0021 §2c) ; un en-tête qui contredit le jeton est rejeté par le cross-check (RLM03).
/// </para>
/// </summary>
internal sealed class HeaderTenantResolver : IClientSuppliedTenantResolver
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

namespace Liakont.Host.MultiTenancy;

using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Resolves tenant from the request hostname subdomain.
/// Expected format: <c>{tenant}.stratum.app</c> or <c>{tenant}.localhost</c>.
/// Returns <c>null</c> if the host has fewer than 3 segments (no subdomain)
/// or if the subdomain is "www".
/// <para>
/// Voie CLIENT-FOURNIE (<see cref="IClientSuppliedTenantResolver"/>) : non autoritaire en realm unique
/// (ADR-0021 §2c) ; un sous-domaine qui contredit le jeton est rejeté par le cross-check (RLM03).
/// </para>
/// </summary>
internal sealed class SubdomainTenantResolver : IClientSuppliedTenantResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubdomainTenantResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? Resolve()
    {
        var host = _httpContextAccessor.HttpContext?.Request.Host.Host;
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        // localhost without subdomain (e.g., "localhost")
        if (!host.Contains('.'))
        {
            return null;
        }

        // IP addresses (e.g., "127.0.0.1", "::1") — no subdomain possible
        if (System.Net.IPAddress.TryParse(host, out _))
        {
            return null;
        }

        var segments = host.Split('.');

        // Need at least 3 segments: subdomain.domain.tld
        // Exception: subdomain.localhost (2 segments with "localhost" as TLD)
        string? subdomain;
        if (segments.Length >= 3)
        {
            subdomain = segments[0];
        }
        else if (segments.Length == 2 && segments[1].Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            subdomain = segments[0];
        }
        else
        {
            return null;
        }

        // Ignore "www" — it's not a tenant
        if (subdomain.Equals("www", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return subdomain;
    }
}

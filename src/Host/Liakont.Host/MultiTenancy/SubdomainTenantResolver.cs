namespace Liakont.Host.MultiTenancy;

using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Resolves tenant from the request hostname subdomain.
/// Expected format: <c>{tenant}.stratum.app</c> or <c>{tenant}.localhost</c>.
/// Returns <c>null</c> if the host has fewer than 3 segments (no subdomain)
/// or if the subdomain is "www".
/// <para>
/// En SaaS mutualisé (mono-host, sans DNS par tenant — ADR-0021 §Conséquences), le sous-domaine est
/// INCIDENT (ex. « app » dans <c>app.liakont.fr</c>) et N'EST PAS un signal de cross-check : il reste
/// une voie de repli pour les déploiements dédiés, mais n'est PAS exposé comme
/// <see cref="IClientSuppliedTenantResolver"/> (réservé aux canaux que le client POSITIONNE
/// DÉLIBÉRÉMENT, comme l'en-tête <c>X-Tenant-Id</c>).
/// </para>
/// </summary>
internal sealed class SubdomainTenantResolver : ITenantResolver
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

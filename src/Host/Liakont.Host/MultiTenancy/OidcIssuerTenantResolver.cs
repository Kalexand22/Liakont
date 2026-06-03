namespace Liakont.Host.MultiTenancy;

using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Resolves tenant from the OIDC <c>iss</c> (issuer) claim by extracting the Keycloak
/// realm name and mapping it to a tenant ID via <see cref="IRealmRegistry"/>.
/// </summary>
/// <remarks>
/// Keycloak issuer URLs follow the pattern <c>{base}/realms/{realm-name}</c>.
/// This resolver extracts the last path segment and looks it up in the dynamic realm registry.
/// Inserted in the resolver chain between subdomain/header and the generic JWT claim resolver.
/// </remarks>
internal sealed class OidcIssuerTenantResolver : ITenantResolver
{
    internal const string IssuerClaimType = "iss";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IRealmRegistry _realmRegistry;

    public OidcIssuerTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        IRealmRegistry realmRegistry)
    {
        _httpContextAccessor = httpContextAccessor;
        _realmRegistry = realmRegistry;
    }

    public string? Resolve()
    {
        var issuer = _httpContextAccessor.HttpContext?.User.FindFirst(IssuerClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(issuer))
        {
            return null;
        }

        var realmName = ExtractRealmName(issuer);
        if (realmName is null)
        {
            return null;
        }

        return _realmRegistry.TryGetTenantId(realmName, out var tenantId)
            ? tenantId
            : null;
    }

    /// <summary>
    /// Extracts the realm name from a Keycloak issuer URL.
    /// Expected format: <c>http(s)://host/realms/{realm-name}</c>.
    /// </summary>
    internal static string? ExtractRealmName(string issuerUrl)
    {
        // Find "/realms/" in the URL and take everything after it
        const string realmsSegment = "/realms/";
        var idx = issuerUrl.LastIndexOf(realmsSegment, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var realmStart = idx + realmsSegment.Length;
        if (realmStart >= issuerUrl.Length)
        {
            return null;
        }

        // Take everything after "/realms/" up to the next "/" or end of string
        var remaining = issuerUrl.AsSpan(realmStart);
        var slashIdx = remaining.IndexOf('/');
        var realmName = slashIdx >= 0
            ? remaining[..slashIdx]
            : remaining;

        return realmName.Length > 0 ? realmName.ToString() : null;
    }
}

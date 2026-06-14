namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Request to provision a Keycloak realm for a tenant.
/// </summary>
public sealed class KeycloakRealmProvisionRequest
{
    /// <summary>Stratum tenant ID (e.g., "acme").</summary>
    public required string TenantId { get; init; }

    /// <summary>Human-readable display name for the realm.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Keycloak realm name (e.g., "stratum-acme").</summary>
    public required string RealmName { get; init; }

    /// <summary>OIDC client secret for the "stratum" client in this realm.</summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// Company ID (Guid) emitted as a HARDCODED <c>company_id</c> claim by the realm's
    /// OIDC client — every user of the realm carries it (one tenant = one company),
    /// so data scoping works without per-user attributes.
    /// </summary>
    public required string CompanyId { get; init; }

    /// <summary>
    /// Allowed redirect URIs for the OIDC client (e.g., "https://localhost:55995/*").
    /// </summary>
    public required IReadOnlyList<string> RedirectUris { get; init; }

    /// <summary>
    /// Allowed web origins for CORS (e.g., "https://localhost:55995").
    /// </summary>
    public required IReadOnlyList<string> WebOrigins { get; init; }
}

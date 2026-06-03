namespace Stratum.Common.Infrastructure.Keycloak;

/// <summary>
/// Configuration for Keycloak Admin REST API access.
/// Used by <see cref="KeycloakAdminTokenService"/> and <see cref="KeycloakRealmProvisioner"/>.
/// Bound from the <c>"Keycloak"</c> configuration section alongside other Keycloak settings.
/// </summary>
public sealed class KeycloakAdminOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>
    /// Base URL of the Keycloak server (e.g., "http://localhost:8080").
    /// Used for Admin REST API calls — NOT the realm-specific authority URL.
    /// </summary>
    public string AdminBaseUrl { get; init; } = string.Empty;

    /// <summary>Admin username for the master realm.</summary>
    public string AdminUsername { get; init; } = "admin";

    /// <summary>Admin password for the master realm.</summary>
    public string AdminPassword { get; init; } = string.Empty;

    /// <summary>
    /// Base URL of the Stratum application, used to construct redirect URIs
    /// for OIDC clients in new realms (e.g., "https://localhost:55995").
    /// </summary>
    public string AppBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// The primary Keycloak realm name used for OIDC browser login (e.g., "stratum-dev").
    /// New tenant subdomains are added as redirect URIs to this realm's client.
    /// </summary>
    public string PrimaryRealmName { get; init; } = string.Empty;

    /// <summary>
    /// Whether Keycloak admin provisioning is configured and available.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AdminBaseUrl)
        && !string.IsNullOrWhiteSpace(AdminUsername)
        && !string.IsNullOrWhiteSpace(AdminPassword);
}

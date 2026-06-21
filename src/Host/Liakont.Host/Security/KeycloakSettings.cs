namespace Liakont.Host.Security;

/// <summary>
/// Configuration for Keycloak OIDC integration.
/// Bound from <c>appsettings.json</c> section <c>"Keycloak"</c>.
/// When <see cref="Authority"/> is non-empty, the OIDC + RS256 JwtBearer pipeline
/// replaces the symmetric JWT pipeline.
/// </summary>
internal sealed class KeycloakSettings
{
    public string Authority { get; init; } = string.Empty;

    /// <summary>
    /// Optional explicit OIDC discovery (metadata) address for the primary realm.
    /// When empty, the handler derives it from <see cref="Authority"/>
    /// (<c>{Authority}/.well-known/openid-configuration</c>).
    /// In the Docker appliance (F12 §6.2), this points at the IdP over the INTERNAL network
    /// (e.g. <c>http://keycloak:8080/realms/liakont/.well-known/openid-configuration</c>) while
    /// <see cref="Authority"/> stays the PUBLIC issuer — the back-channel (discovery, token, JWKS)
    /// then resolves internally (no hairpin through the reverse proxy), and the public
    /// authorization endpoint / issuer remain those advertised by Keycloak's frontend hostname.
    /// </summary>
    public string MetadataAddress { get; init; } = string.Empty;

    public string ClientId { get; init; } = "stratum";

    public string ClientSecret { get; init; } = string.Empty;

    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>
    /// Feature flag to control Keycloak login flow. When <c>true</c> and
    /// <see cref="Authority"/> is configured, the login page redirects to Keycloak
    /// instead of showing the local login form. Set to <c>false</c> to use
    /// the legacy username/password form even when Keycloak middleware is active.
    /// Default: <c>true</c>.
    /// </summary>
    public bool UseKeycloak { get; init; } = true;

    /// <summary>
    /// URI to redirect to after Keycloak end_session completes. Defaults to <c>/login</c>.
    /// Must be registered in the Keycloak client's "Valid Post Logout Redirect URIs".
    /// </summary>
    public string PostLogoutRedirectUri { get; init; } = "/login";

    /// <summary>
    /// Maps realm names to tenant IDs for multi-realm routing.
    /// Key = Keycloak realm name (e.g., "stratum-enterprise"), Value = Stratum tenant ID.
    /// When a JWT's <c>iss</c> claim ends with a known realm name, the corresponding tenant ID is resolved.
    /// </summary>
    public Dictionary<string, string> RealmTenantMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Additional realm configurations for multi-realm OIDC support.
    /// Key = realm name, Value = realm-specific OIDC settings.
    /// The primary realm uses <see cref="Authority"/>/<see cref="ClientId"/>/<see cref="ClientSecret"/>.
    /// Additional realms are configured here.
    /// </summary>
    public Dictionary<string, KeycloakRealmConfig> AdditionalRealms { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Base URL of the Keycloak server (e.g., "http://localhost:8080").
    /// Used for Admin REST API calls — NOT the realm-specific authority URL.
    /// </summary>
    public string AdminBaseUrl { get; init; } = string.Empty;

    /// <summary>Admin username for the master realm (used for realm provisioning).</summary>
    public string AdminUsername { get; init; } = "admin";

    /// <summary>Admin password for the master realm (used for realm provisioning).</summary>
    public string AdminPassword { get; init; } = string.Empty;

    /// <summary>
    /// Base URL of the Stratum application, used to construct redirect URIs
    /// for OIDC clients in new realms (e.g., "https://localhost:55995").
    /// </summary>
    public string AppBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// When <c>true</c>, Keycloak OIDC middleware is registered (Authority is set).
    /// This controls middleware registration, not the login flow — see <see cref="UseKeycloak"/>.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority);

    /// <summary>
    /// Fenêtre de révocation maximale des permissions <b>sensibles</b>
    /// (<see cref="SensitivePermissions"/> : <c>liakont.actions</c>, <c>liakont.settings</c>).
    /// Une session non super-admin porteuse d'une telle permission reçoit un cap de durée ABSOLU
    /// (sans glissement) égal à cette valeur : à l'échéance, le cookie est rejeté et l'utilisateur est
    /// ré-authentifié via OIDC (transparent tant que la session SSO Keycloak vit), ce qui rejoue la
    /// projection rôle→permission — bornant ainsi la fenêtre de révocation à cette durée au lieu des
    /// ≥ 8 h de la fenêtre glissante (ADR-0017 §Négatif, atténuation RDF10/DEC-6). Défaut défendable :
    /// 30 min. Tunable par l'opérateur (plus court = révocation plus stricte ; plus long = moins de
    /// ré-auth). Les permissions non sensibles gardent la fenêtre glissante par défaut.
    /// Doit être &gt; 0 (validé au démarrage par <c>ValidateConfiguration</c>).
    /// </summary>
    public TimeSpan SensitivePermissionRevocationWindow { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// When <c>true</c>, the login page redirects to Keycloak and logout
    /// triggers Keycloak end_session. Requires both <see cref="IsConfigured"/>
    /// and <see cref="UseKeycloak"/> to be true.
    /// </summary>
    public bool IsKeycloakLoginActive => IsConfigured && UseKeycloak;
}

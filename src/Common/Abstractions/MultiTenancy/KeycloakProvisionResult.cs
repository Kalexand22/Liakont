namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Result of a Keycloak realm provisioning operation.
/// </summary>
public sealed class KeycloakProvisionResult
{
    private KeycloakProvisionResult()
    {
    }

    public bool Success { get; private init; }

    public bool AlreadyProvisioned { get; private init; }

    /// <summary>Keycloak realm name (e.g., "stratum-acme").</summary>
    public string? RealmName { get; private init; }

    /// <summary>Full issuer/authority URL (e.g., "http://localhost:8080/realms/stratum-acme").</summary>
    public string? Authority { get; private init; }

    /// <summary>OIDC client secret used for the realm.</summary>
    public string? ClientSecret { get; private init; }

    public string? ErrorMessage { get; private init; }

    public static KeycloakProvisionResult Created(string realmName, string authority, string clientSecret) =>
        new() { Success = true, RealmName = realmName, Authority = authority, ClientSecret = clientSecret };

    public static KeycloakProvisionResult Idempotent(string realmName, string authority) =>
        new() { Success = true, AlreadyProvisioned = true, RealmName = realmName, Authority = authority };

    public static KeycloakProvisionResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}

namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Provisions and deprovisions Keycloak realms for tenant isolation.
/// </summary>
public interface IKeycloakRealmProvisioner
{
    /// <summary>
    /// Creates a Keycloak realm with OIDC client, protocol mappers, and admin user.
    /// Idempotent: returns success if the realm already exists.
    /// </summary>
    Task<KeycloakProvisionResult> ProvisionRealmAsync(
        KeycloakRealmProvisionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a Keycloak realm (cascades all clients, users, roles).
    /// Best-effort — logs and swallows errors.
    /// </summary>
    Task DeleteRealmAsync(string realmName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a subdomain-based redirect URI to the primary realm's OIDC client
    /// so that browser login works for the new tenant's subdomain.
    /// </summary>
    Task AddTenantRedirectUriAsync(
        string primaryRealmName,
        string tenantSubdomain,
        CancellationToken cancellationToken = default);
}

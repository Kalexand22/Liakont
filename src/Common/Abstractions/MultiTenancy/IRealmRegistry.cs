namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Thread-safe, in-memory registry of known Keycloak realms and their tenant mappings.
/// Seeded at startup and updated at runtime when new tenants are provisioned.
/// </summary>
public interface IRealmRegistry
{
    /// <summary>
    /// Returns <c>true</c> if the issuer URL belongs to a known realm.
    /// Used by the custom <c>IssuerValidator</c> in JWT Bearer validation.
    /// </summary>
    bool IsKnownIssuer(string issuer);

    /// <summary>
    /// Attempts to resolve a tenant ID from a Keycloak realm name.
    /// Used by <see cref="Stratum.Common.Abstractions.MultiTenancy.IRealmRegistry"/>
    /// </summary>
    bool TryGetTenantId(string realmName, out string? tenantId);

    /// <summary>
    /// Registers a new realm at runtime (after tenant provisioning).
    /// Also triggers JWKS key resolver registration for immediate JWT validation.
    /// </summary>
    void RegisterRealm(string realmName, string tenantId, string authority);

    /// <summary>
    /// Unregisters a realm at runtime (after tenant deactivation).
    /// Removes the realm-to-tenant mapping and the issuer from the known set.
    /// </summary>
    void UnregisterRealm(string realmName, string authority);
}

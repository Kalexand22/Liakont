// Liakont addition (OPS03 §4.25): provisioning utilisateur Keycloak - not part of the original Stratum vendoring.
namespace Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Provisions individual users in an EXISTING Keycloak realm via the Admin REST API —
/// the per-user counterpart of <see cref="IKeycloakRealmProvisioner"/> (which only creates
/// the initial admin as part of realm creation). Consumed by the host's tenant-user
/// provisioning layer; callers compose the full flow (create, role, attributes, password)
/// and use <see cref="DeleteUserAsync"/> as compensation when a later step fails.
/// </summary>
public interface IKeycloakUserProvisioner
{
    /// <summary>Returns the Keycloak user id for an exact username match in the realm, or <c>null</c>.</summary>
    Task<string?> FindUserIdByUsernameAsync(string realmName, string username, CancellationToken cancellationToken = default);

    /// <summary>Creates a user in the realm and returns its Keycloak id (the future OIDC <c>sub</c>).</summary>
    Task<string> CreateUserAsync(string realmName, KeycloakUserSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Sets (replaces) attributes on an existing user — e.g. <c>stratum_user_id</c>.</summary>
    Task SetUserAttributesAsync(
        string realmName,
        string userId,
        IReadOnlyDictionary<string, string> attributes,
        CancellationToken cancellationToken = default);

    /// <summary>Resets the user's password; <paramref name="temporary"/> forces a change at next login.</summary>
    Task ResetPasswordAsync(string realmName, string userId, string password, bool temporary, CancellationToken cancellationToken = default);

    /// <summary>Ensures a realm role exists (idempotent: an already-existing role is a success).</summary>
    Task EnsureRealmRoleAsync(string realmName, string roleName, string description, CancellationToken cancellationToken = default);

    /// <summary>Assigns existing realm roles to the user (unknown role names are ignored).</summary>
    Task AssignRealmRolesAsync(string realmName, string userId, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default);

    /// <summary>Deletes the user — compensation path when a step after creation fails.</summary>
    Task DeleteUserAsync(string realmName, string userId, CancellationToken cancellationToken = default);
}

namespace Liakont.Host.Security;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

/// <summary>
/// Catalogue IMMUABLE rôle→permission : unique matérialisation en code de la matrice §3 de
/// <c>docs/architecture/identity-permissions-liakont.md</c> (décision ADR-0017). Source de vérité
/// unique — AUCUNE valeur n'est inventée : les 4 rôles realm (§2) et les 4 permissions
/// (<see cref="LiakontPermissions"/>) proviennent du document. Les permissions Liakont sont
/// entièrement dérivées des rôles : un utilisateur n'a d'autres permissions que celles que ses
/// rôles realm lui accordent.
/// </summary>
/// <remarks>
/// Catalogue IdP-agnostique (aucun appel Keycloak-spécifique) : la projection vit dans la couche
/// d'auth du Host, derrière l'abstraction D10 ; un IdP alternatif réutilise la même projection.
/// </remarks>
internal static class RolePermissionCatalog
{
    /// <summary>
    /// Type de claim portant une permission Liakont. L'UI (<see cref="ClaimsPermissionService"/>)
    /// ET les endpoints (<see cref="PermissionAuthorizationHandler"/>) lisent ce même claim :
    /// mécanisme d'autorisation unique (INV-IDN01-3).
    /// </summary>
    public const string PermissionClaimType = "permission";

    // Matrice §3 (clés = rôles realm Keycloak §2, comparaison insensible à la casse) :
    //   lecture     → read
    //   operateur   → read + actions
    //   parametrage → read + actions + settings
    //   superviseur → read + actions + settings + supervision
    private static readonly Dictionary<string, string[]> RoleToPermissions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["lecture"] = [LiakontPermissions.Read],
            ["operateur"] = [LiakontPermissions.Read, LiakontPermissions.Actions],
            ["parametrage"] = [LiakontPermissions.Read, LiakontPermissions.Actions, LiakontPermissions.Settings],
            ["superviseur"] =
            [
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.Settings,
                LiakontPermissions.Supervision,
            ],
        };

    // Types de claim portant les rôles realm. Sous OIDC le RoleClaimType est "roles" (mapper realm
    // roles, realm-export.json) ; selon le provider/cookie les rôles peuvent aussi arriver sous
    // ClaimTypes.Role ou "role" — on lit les trois (cf. SuperAdminRoles, même prudence).
    private static readonly string[] RoleClaimTypes = [ClaimTypes.Role, "role", "roles"];

    /// <summary>
    /// Permissions (union dédupliquée, insensible à la casse) accordées par l'ensemble de rôles
    /// realm donné. Un rôle inconnu du catalogue n'accorde aucune permission.
    /// </summary>
    public static IReadOnlyCollection<string> PermissionsForRoles(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            if (role is not null && RoleToPermissions.TryGetValue(role, out var rolePermissions))
            {
                foreach (var permission in rolePermissions)
                {
                    permissions.Add(permission);
                }
            }
        }

        return permissions;
    }

    /// <summary>Permissions accordées par les rôles realm portés par le principal.</summary>
    public static IReadOnlyCollection<string> PermissionsForPrincipal(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return PermissionsForRoles(ReadRoles(user.Claims));
    }

    /// <summary>
    /// Projette les rôles realm de l'identité en claims <see cref="PermissionClaimType"/> (matrice
    /// §3) sur cette même identité. Appelé à l'ouverture de session OIDC (couche d'auth, derrière
    /// l'abstraction D10) — ADR-0017. Idempotent : aucun claim de permission n'est dupliqué si la
    /// projection a déjà été appliquée (une transformation de claims peut rejouer).
    /// </summary>
    public static void ProjectPermissionClaims(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var existing = identity.FindAll(PermissionClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var permission in PermissionsForRoles(ReadRoles(identity.Claims)))
        {
            if (existing.Add(permission))
            {
                identity.AddClaim(new Claim(PermissionClaimType, permission));
            }
        }
    }

    private static IEnumerable<string> ReadRoles(IEnumerable<Claim> claims) =>
        claims.Where(claim => RoleClaimTypes.Contains(claim.Type)).Select(claim => claim.Value);
}

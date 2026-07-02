namespace Liakont.Host.Security;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

/// <summary>
/// Catalogue IMMUABLE rôle→permission : unique matérialisation en code de la matrice §3 de
/// <c>docs/architecture/identity-permissions-liakont.md</c> (décision ADR-0017). Source de vérité
/// unique — AUCUNE valeur n'est inventée : les rôles realm (§2) et les permissions
/// (<see cref="LiakontPermissions"/>) proviennent du document, y compris les colonnes GED
/// (<c>ged.read</c> / <c>ged.export</c> / <c>ged.confidential</c>) amendées par GED06 (F19 §6.5).
/// Les permissions Liakont sont entièrement dérivées des rôles : un utilisateur n'a d'autres
/// permissions que celles que ses rôles realm lui accordent.
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
    //   lecture     → read                                                     + ged.read
    //   operateur   → read + actions                                           + ged.read + ged.export
    //   parametrage → read + actions + settings                                + ged.read + ged.export
    //   superviseur → read + actions + settings + supervision + instance.settings + ged.read + ged.export + ged.confidential
    //   exploitant  → fleet  (rôle IT Innovations HORS matrice éditeur §3 — méta-supervision de flotte,
    //                          OPS04 ; n'accorde AUCUNE permission éditeur, seulement le dashboard de flotte)
    //
    // instance.settings (ADR-0039) : paramétrage MUTANT d'instance (config email), accordé à l'opérateur
    // d'instance (superviseur). Distinct de supervision (lecture seule) — voir identity-permissions-liakont.md §3.
    //
    // Amendement GED (GED06, F19 §6.5, ADR-0032/0035/0036) : les 3 permissions Liakont dédiées de la GED
    // sont matérialisées EN CODE (const + Dictionary) — pas du paramétrage tenant, pas une règle inventée,
    // jamais une permission socle accordée à un rôle Liakont (FIX07c/RL-35). Tiers = moindre privilège sur
    // le modèle §3 (voir identity-permissions-liakont.md §3, colonnes GED) :
    //   - ged.read  = consultation GED → tier consultation (comme read), à partir de `lecture` ;
    //   - ged.export = export journalisé, gardé SÉPARÉMENT de read (ADR-0036 §4) → à partir d'`operateur`
    //     (le tier des actions) ; `lecture` (consultation pure) ne l'a pas ;
    //   - ged.confidential = axes/entités confidentiels (le plus sensible, ADR-0035 INV-GED-10) → `superviseur`
    //     seul (moindre privilège ; élargir = avenant délibéré du document §3, jamais un rétrécissement post-fuite).
    private static readonly Dictionary<string, string[]> RoleToPermissions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["lecture"] = [LiakontPermissions.Read, LiakontPermissions.GedRead],
            ["operateur"] =
            [
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
            ],
            ["parametrage"] =
            [
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.Settings,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
            ],
            ["superviseur"] =
            [
                LiakontPermissions.Read,
                LiakontPermissions.Actions,
                LiakontPermissions.Settings,
                LiakontPermissions.Supervision,
                LiakontPermissions.GedRead,
                LiakontPermissions.GedExport,
                LiakontPermissions.GedConfidential,
                LiakontPermissions.InstanceSettings,
            ],
            ["exploitant"] = [LiakontPermissions.Fleet],
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

        // Le catalogue §3 est l'UNIQUE source autoritative des permissions à l'exécution : on retire
        // d'abord tout claim "permission" préexistant (défense en profondeur contre un futur mapper ou
        // attribut IdP nommé "permission" qui forgerait une permission hors matrice §3), puis on projette
        // les permissions dérivées des rôles realm. Idempotent (rejouable par une transformation de claims).
        foreach (var stale in identity.FindAll(PermissionClaimType).ToList())
        {
            identity.RemoveClaim(stale);
        }

        foreach (var permission in PermissionsForRoles(ReadRoles(identity.Claims)))
        {
            identity.AddClaim(new Claim(PermissionClaimType, permission));
        }
    }

    private static IEnumerable<string> ReadRoles(IEnumerable<Claim> claims) =>
        claims.Where(claim => RoleClaimTypes.Contains(claim.Type)).Select(claim => claim.Value);
}

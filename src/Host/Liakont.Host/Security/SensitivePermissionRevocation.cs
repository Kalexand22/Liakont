namespace Liakont.Host.Security;

using System;
using System.Security.Claims;

/// <summary>
/// Décide si une session OIDC/cookie doit recevoir un <b>cap de durée absolu court, sans glissement</b>
/// (atténuation RDF10 / DEC-6 de la fenêtre de révocation décrite dans ADR-0017 §Négatif).
/// </summary>
/// <remarks>
/// Problème borné ici : la garde de permission lit des claims figés au sign-in, et le cookie est en
/// expiration <i>glissante</i> (8 h) qui se ré-émet SANS rejouer la projection rôle→permission — une
/// révocation de rôle Keycloak n'est donc pas honorée avant une ré-authentification complète (fenêtre
/// ≥ 8 h, non bornée pour une session active). Pour les permissions <b>sensibles</b>
/// (<see cref="SensitivePermissions"/>), on plafonne la session à une fenêtre courte et on DÉSACTIVE le
/// glissement (<c>AllowRefresh = false</c>) : à l'échéance, le cookie est rejeté et l'utilisateur est
/// ré-authentifié via OIDC (transparent tant que la session SSO Keycloak est vivante), ce qui rejoue la
/// projection sur les rôles COURANTS — la révocation est alors honorée en ≤ fenêtre.
/// <para>
/// Décision PURE et testable (aucun I/O) : le point d'accrochage (<c>OnTokenValidated</c> de la couche
/// d'auth Keycloak, D10) applique le résultat sur les <c>AuthenticationProperties</c> de la connexion.
/// Le court-circuit super-admin (<see cref="SuperAdminRoles"/>) est préservé : un super-admin n'est
/// jamais plafonné. Les sessions sans permission sensible gardent la fenêtre glissante par défaut.
/// </para>
/// </remarks>
internal static class SensitivePermissionRevocation
{
    /// <summary>
    /// Détermine le cap de session pour le principal donné. Plafonne (sans glissement) lorsqu'il s'agit
    /// d'un non super-admin porteur d'au moins une permission sensible ; sinon, aucun cap (le défaut
    /// glissant s'applique).
    /// </summary>
    /// <param name="principal">Principal après projection des claims permission (ADR-0017).</param>
    /// <param name="now">Instant courant (injecté pour la testabilité).</param>
    /// <param name="window">Fenêtre de révocation maximale des permissions sensibles (&gt; 0).</param>
    public static Decision Resolve(ClaimsPrincipal principal, DateTimeOffset now, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // Super-admin : jamais plafonné (court-circuit cohérent avec UI + endpoints).
        if (SuperAdminRoles.IsSuperAdmin(principal))
        {
            return new Decision(false, null);
        }

        foreach (var claim in principal.FindAll(RolePermissionCatalog.PermissionClaimType))
        {
            if (SensitivePermissions.IsSensitive(claim.Value))
            {
                return new Decision(true, now + window);
            }
        }

        return new Decision(false, null);
    }

    /// <summary>Résultat de la décision : faut-il plafonner, et à quelle échéance absolue.</summary>
    internal readonly record struct Decision(bool Cap, DateTimeOffset? ExpiresUtc);
}

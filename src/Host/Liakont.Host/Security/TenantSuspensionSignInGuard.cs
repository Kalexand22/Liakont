namespace Liakont.Host.Security;

using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.MultiTenancy;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Décision de refus au SIGN-IN d'un utilisateur dont le tenant est suspendu (OPS03.4 lot B),
/// extraite de l'événement OIDC pour être testable sans flux OIDC réel : issuer du jeton → realm →
/// tenant (registre) → statut. Un super-admin n'est JAMAIS refusé (l'opérateur d'instance doit
/// pouvoir réactiver) ; un issuer absent/inconnu ne refuse jamais (ce garde ne décide que de la
/// suspension, pas de la validité du jeton). IdP-agnostique : seul le POINT d'accrochage
/// (OnTokenValidated) est Keycloak.
/// </summary>
internal static class TenantSuspensionSignInGuard
{
    /// <summary><c>true</c> si la connexion doit être refusée (tenant suspendu, utilisateur non super-admin).</summary>
    public static async Task<bool> ShouldRefuseAsync(
        ClaimsPrincipal principal,
        IRealmRegistry realmRegistry,
        ITenantSuspensionLookup suspensionLookup,
        CancellationToken cancellationToken)
    {
        if (SuperAdminRoles.IsSuperAdmin(principal))
        {
            return false;
        }

        if (principal.FindFirst("iss")?.Value is not { } issuer
            || OidcIssuerTenantResolver.ExtractRealmName(issuer) is not { } realmName)
        {
            return false;
        }

        if (!realmRegistry.TryGetTenantId(realmName, out var tenantId) || tenantId is null)
        {
            return false;
        }

        return await suspensionLookup.IsSuspendedAsync(tenantId, cancellationToken);
    }
}

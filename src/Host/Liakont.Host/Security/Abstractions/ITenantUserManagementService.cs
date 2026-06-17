namespace Liakont.Host.Security.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Gestion (au-delà de la création) des utilisateurs d'un TENANT depuis la console, SANS jamais ouvrir
/// la console d'admin Keycloak (RB4) : lister les utilisateurs du tenant et réinitialiser un mot de
/// passe. La CRÉATION reste portée par <see cref="ITenantUserProvisioningService"/> (réutilisée par la
/// page). Abstraction PRODUIT, IdP-agnostique (Keycloak = une implémentation derrière la couche d'auth,
/// blueprint §6). TENANT-SCOPÉE (CLAUDE.md n°9/17) : un utilisateur n'est listé/réinitialisé que s'il
/// porte le <c>company_id</c> du tenant ciblé — jamais de fuite cross-tenant.
/// </summary>
public interface ITenantUserManagementService
{
    /// <summary>
    /// Liste les utilisateurs du tenant (comptes IdP du realm portant son <c>company_id</c>). Liste vide
    /// si le tenant n'a pas encore d'utilisateur. Échec → <see cref="System.InvalidOperationException"/>
    /// (configuration IdP absente, tenant introuvable…), message opérateur en français.
    /// </summary>
    Task<IReadOnlyList<TenantUserLine>> ListUsersAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Réinitialise le mot de passe d'un utilisateur du tenant (mot de passe temporaire, changement forcé
    /// au prochain login). L'utilisateur DOIT appartenir au tenant (<c>company_id</c>) — sinon refus. Le
    /// mot de passe temporaire n'est JAMAIS loggé (règle n°18) : envoyé par email si SMTP configuré, sinon
    /// remis UNE FOIS dans le résultat.
    /// </summary>
    Task<TenantUserPasswordResetResult> ResetPasswordAsync(
        string tenantId, string idpUserId, CancellationToken cancellationToken = default);
}

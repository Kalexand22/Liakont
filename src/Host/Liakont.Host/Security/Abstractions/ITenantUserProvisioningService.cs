namespace Liakont.Host.Security.Abstractions;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provisionne un utilisateur de TENANT de bout en bout : compte chez le fournisseur d'identité
/// (realm du tenant), rôle realm standard, rattachement au scope de données du tenant
/// (<c>company_id</c>) et compte applicatif (<c>identity.users</c> de la base tenant), puis
/// invitation par email. Abstraction PRODUIT, IdP-agnostique (Keycloak = une implémentation,
/// derrière la couche d'auth — blueprint §6) : consommée par l'endpoint d'administration et par
/// l'assistant console « Nouveau client », qui ne voient jamais l'IdP concret.
/// </summary>
public interface ITenantUserProvisioningService
{
    /// <summary>Provisionne l'utilisateur. Échec = résultat porteur d'un message opérateur en français.</summary>
    Task<TenantUserProvisionResult> ProvisionUserAsync(
        TenantUserProvisionRequest request,
        CancellationToken cancellationToken = default);
}

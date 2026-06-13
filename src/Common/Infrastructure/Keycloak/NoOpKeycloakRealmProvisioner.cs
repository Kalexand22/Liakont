// Liakont addition (RLM04) — fichier AJOUTÉ (non épinglé par le baseline socle §4.12) : voir
// docs/architecture/provenance-socle-stratum.md §4.28. Consigné pour la re-convergence NuGet.
namespace Stratum.Common.Infrastructure.Keycloak;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation <see cref="IKeycloakRealmProvisioner"/> <b>no-op</b> du <b>profil SaaS partagé</b>
/// (ADR-0021 §1). En realm Keycloak unique partagé, le provisioning d'un tenant ne crée NI realm NI
/// client par tenant : tous les utilisateurs vivent dans le realm partagé et l'isolation repose sur
/// le claim <c>company_id</c> par-utilisateur (mapper d'attribut) + le cross-check global fail-closed
/// (RLM03), pas sur une frontière de realm.
/// <para>
/// Enregistrée par défaut en DI (<see cref="Database.ServiceCollectionExtensions.AddStratumDatabase"/>) ;
/// le déploiement <b>dédié mono-tenant</b> garde la vraie implémentation
/// <see cref="KeycloakRealmProvisioner"/> via <c>Keycloak:DedicatedRealmPerTenant=true</c>.
/// </para>
/// <para>
/// <see cref="ProvisionRealmAsync"/> renvoie <see cref="KeycloakProvisionResult.Idempotent"/>
/// (« déjà provisionné », rien à faire) <b>sans aucun appel HTTP</b> — donc aucun <c>POST /admin/realms</c>
/// n'est émis (INV-0021-1). Dans <see cref="Database.TenantProvisioningService.ProvisionAsync"/>,
/// <c>AlreadyProvisioned=true</c> ⇒ <c>realmCreated=false</c> ⇒ ni enregistrement de realm ni redirect
/// par tenant (nettoyage vestigial gardé par le seam, pas par une suppression de code).
/// </para>
/// </summary>
internal sealed class NoOpKeycloakRealmProvisioner : IKeycloakRealmProvisioner
{
    /// <inheritdoc />
    public Task<KeycloakProvisionResult> ProvisionRealmAsync(
        KeycloakRealmProvisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Aucun realm/client n'est créé en profil partagé. On signale « déjà provisionné » : l'autorité
        // n'est jamais consommée (RegisterRealm est court-circuité côté appelant par realmCreated=false).
        return Task.FromResult(KeycloakProvisionResult.Idempotent(request.RealmName, string.Empty));
    }

    /// <inheritdoc />
    public Task DeleteRealmAsync(string realmName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task AddTenantRedirectUriAsync(
        string primaryRealmName,
        string tenantSubdomain,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

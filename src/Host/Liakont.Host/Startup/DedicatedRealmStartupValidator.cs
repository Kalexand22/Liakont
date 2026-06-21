namespace Liakont.Host.Startup;

/// <summary>
/// Validation au DÉMARRAGE du flag de profil de déploiement <c>Keycloak:DedicatedRealmPerTenant</c>
/// (RDF11, redline ADR fondateurs RL-IDP-8). Sur le même modèle que
/// <c>SignatureProviderStartupValidator</c> et la validation de l'abstraction IdP : pur (testable
/// sans le Host), fail-closed, message opérateur français.
/// <para>
/// Le flag sélectionne le profil de déploiement (ADR-0021 §1) :
/// <list type="bullet">
///   <item><c>false</c> (défaut) — SaaS PARTAGÉ : un realm Keycloak unique, isolation par claim
///   <c>company_id</c>. Le provisioning de tenant ne crée NI realm NI client par tenant
///   (no-op DI). C'est le profil dont les invariants INV-0021-1..10 sont prouvés.</item>
///   <item><c>true</c> — DÉDIÉ mono-tenant : chaque tenant a son propre realm, créé par le vrai
///   <c>KeycloakRealmProvisioner</c>. Cette capacité réutilise le provisioning realm-par-tenant du
///   socle et est LATENTE — hors périmètre des invariants INV-0021-* tant qu'elle n'a pas son
///   propre jeu de preuves (voir l'avenant RDF11 de l'ADR-0021).</item>
/// </list>
/// </para>
/// <para>
/// Cohérence vérifiée (fail-closed) : en profil DÉDIÉ, le provisioning d'un realm par tenant exige
/// l'API Admin de Keycloak (URL/identifiants admin). Sans elle, le vrai provisioner est bien
/// enregistré en DI (<c>ServiceCollectionExtensions</c>) mais ne peut NI créer NI gérer le realm du
/// tenant → la capacité serait à moitié activée et échouerait silencieusement au provisioning. On
/// bloque le démarrage plutôt que d'activer une capacité sans son pré-requis (« aucune capacité
/// activée sans preuve »). En profil PARTAGÉ (défaut), aucune exigence supplémentaire : l'API Admin
/// y est optionnelle (le realm partagé n'est pas provisionné par tenant).
/// </para>
/// </summary>
public static class DedicatedRealmStartupValidator
{
    /// <summary>
    /// Valide la cohérence du profil de déploiement choisi par <c>Keycloak:DedicatedRealmPerTenant</c>.
    /// No-op en profil PARTAGÉ (<paramref name="dedicatedRealmPerTenant"/> = <c>false</c>). En profil
    /// DÉDIÉ, lève une <see cref="InvalidOperationException"/> (message opérateur FR) si l'API Admin de
    /// Keycloak n'est pas configurée (pré-requis du provisioning realm-par-tenant).
    /// </summary>
    /// <param name="dedicatedRealmPerTenant">Valeur du flag <c>Keycloak:DedicatedRealmPerTenant</c>.</param>
    /// <param name="keycloakAdminConfigured">
    /// <c>true</c> si l'API Admin de Keycloak est configurée
    /// (<c>KeycloakAdminOptions.IsConfigured</c> : URL + identifiants admin renseignés).
    /// </param>
    public static void Validate(bool dedicatedRealmPerTenant, bool keycloakAdminConfigured)
    {
        if (!dedicatedRealmPerTenant)
        {
            // Profil SaaS PARTAGÉ (défaut) : pas de realm par tenant, l'API Admin est optionnelle.
            return;
        }

        if (!keycloakAdminConfigured)
        {
            throw new InvalidOperationException(
                "Profil de déploiement DÉDIÉ activé (Keycloak:DedicatedRealmPerTenant=true) mais l'API "
                + "Admin de Keycloak n'est pas configurée (Keycloak:AdminBaseUrl / AdminUsername / "
                + "AdminPassword). Ce profil crée un realm par tenant et exige l'API Admin pour le "
                + "provisionner. Renseignez la configuration Admin de Keycloak, ou repassez en profil "
                + "SaaS partagé (Keycloak:DedicatedRealmPerTenant=false).");
        }
    }
}

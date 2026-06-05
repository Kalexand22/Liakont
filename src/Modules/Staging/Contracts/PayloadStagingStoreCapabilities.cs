namespace Liakont.Modules.Staging.Contracts;

/// <summary>
/// Capacités DÉCLARÉES d'un backend de staging (ADR-0014 ; même patron à capacités que le coffre
/// d'archive — blueprint §2 règle 6, §6 ; module-rules §5). Le module pilote son comportement par ces
/// capacités, JAMAIS par un test de type concret (<c>if (store is …)</c> interdit — P1, CLAUDE.md n°14).
/// Elles rapportent les protections NATIVES du backend (chiffrement serveur, expiration native), utilisées
/// EN PLUS des garanties produit (chiffrement applicatif au repos, purge explicite) qui restent la
/// référence et ne dépendent jamais du backend.
///
/// Distinction structurelle avec <c>IArchiveStore</c> : le staging est un magasin TRANSITOIRE de
/// traitement, <b>purgeable</b> (l'interface expose <see cref="IPayloadStagingStore.PurgeAsync"/>) ; le
/// coffre d'archive reste WORM/immuable (aucune méthode de suppression). Cette différence est dans la
/// FORME des interfaces, pas dans un drapeau de capacité.
/// </summary>
/// <param name="SupportsNativeEncryption">
/// Le backend chiffre nativement au repos (ex. S3 SSE). Le chiffrement applicatif tenant-scopé reste
/// appliqué EN PLUS — l'intégrité/confidentialité produit ne dépend jamais du backend.
/// </param>
/// <param name="SupportsNativeExpiry">
/// Le backend offre une expiration/cycle de vie natif (ex. S3 lifecycle). La purge produit explicite
/// (subordonnée à la présence WORM — ADR-0014 §4) reste la référence.
/// </param>
public readonly record struct PayloadStagingStoreCapabilities(bool SupportsNativeEncryption, bool SupportsNativeExpiry)
{
    /// <summary>Aucune capacité native (ex. système de fichiers d'appliance) : chiffrement applicatif + purge explicite.</summary>
    public static PayloadStagingStoreCapabilities None => new(SupportsNativeEncryption: false, SupportsNativeExpiry: false);
}

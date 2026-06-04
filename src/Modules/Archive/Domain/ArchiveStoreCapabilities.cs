namespace Liakont.Modules.Archive.Domain;

/// <summary>
/// Capacités DÉCLARÉES d'un backend de coffre (blueprint §2 règle 6, §6 ; module-rules §5). Le module
/// Archive pilote son comportement par ces capacités, JAMAIS par un test de type concret
/// (<c>if (store is S3)</c> interdit — P1, CLAUDE.md n°14). Elles servent à RAPPORTER les protections
/// natives actives (manifest, rapport d'intégrité), pas à brancher l'intégrité produit : la chaîne de
/// hashes + les addenda chaînés restent l'intégrité de référence, indépendante du verrou natif du
/// backend (ceinture + bretelles).
/// </summary>
/// <param name="SupportsObjectLock">
/// Le backend offre un verrou objet natif en mode conformité (vrai WORM matériel — ex. S3 Object Lock,
/// Azure immutable blob, GCS bucket lock). Utilisé EN PLUS de la chaîne de hashes quand présent.
/// </param>
/// <param name="SupportsLegalHold">Le backend offre une rétention légale (legal hold) activable.</param>
public readonly record struct ArchiveStoreCapabilities(bool SupportsObjectLock, bool SupportsLegalHold)
{
    /// <summary>Aucune capacité native (ex. système de fichiers d'appliance) : l'intégrité repose sur la chaîne de hashes.</summary>
    public static ArchiveStoreCapabilities None => new(SupportsObjectLock: false, SupportsLegalHold: false);
}

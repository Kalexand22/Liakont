namespace Liakont.Agent.Updater;

/// <summary>
/// Sauvegarde / remplacement / restauration des binaires de l'agent (ADR-0013). Les anciens binaires
/// sont CONSERVÉS (sauvegarde) jusqu'au redémarrage sain de la nouvelle version, pour permettre le
/// rollback. Couture testable (pas d'I/O réel en test).
/// </summary>
public interface IBinarySwapper
{
    /// <summary>Sauvegarde les binaires courants du dossier d'installation vers le dossier de sauvegarde.</summary>
    /// <param name="installDirectory">Dossier d'installation courant.</param>
    /// <param name="backupDirectory">Dossier de sauvegarde (créé).</param>
    void Backup(string installDirectory, string backupDirectory);

    /// <summary>Remplace les binaires du dossier d'installation par ceux du staging (vérifiés).</summary>
    /// <param name="stagingDirectory">Dossier des nouveaux binaires.</param>
    /// <param name="installDirectory">Dossier d'installation.</param>
    void Apply(string stagingDirectory, string installDirectory);

    /// <summary>Restaure les binaires sauvegardés dans le dossier d'installation (rollback).</summary>
    /// <param name="backupDirectory">Dossier de sauvegarde.</param>
    /// <param name="installDirectory">Dossier d'installation.</param>
    void Restore(string backupDirectory, string installDirectory);
}

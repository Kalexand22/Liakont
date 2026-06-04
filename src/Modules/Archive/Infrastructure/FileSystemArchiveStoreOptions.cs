namespace Liakont.Modules.Archive.Infrastructure;

/// <summary>
/// Configuration du coffre sur système de fichiers (store par défaut de l'appliance self-hosted). La
/// racine est un paramètre d'INSTANCE (volume dédié, sauvegarde, rétention 10 ans) — jamais une donnée
/// client en dur (CLAUDE.md n°7). Section de configuration : <c>Archive:Storage:FileSystem</c>.
/// </summary>
public sealed class FileSystemArchiveStoreOptions
{
    /// <summary>Répertoire racine du coffre. Un sous-répertoire est créé par tenant.</summary>
    public string RootPath { get; set; } = string.Empty;
}

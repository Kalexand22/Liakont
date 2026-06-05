namespace Liakont.Modules.Staging.Infrastructure;

/// <summary>
/// Options du magasin de staging FileSystem (section de configuration <c>Staging:Storage:FileSystem</c>).
/// La racine est un PARAMÉTRAGE d'INSTANCE (jamais en dur — CLAUDE.md n°7) : une instance de production
/// configure un volume dédié, distinct du coffre d'archive (le staging est transitoire et purgeable).
/// </summary>
public sealed class FileSystemPayloadStagingStoreOptions
{
    /// <summary>Racine du magasin de staging sur le système de fichiers de l'instance.</summary>
    public string RootPath { get; set; } = string.Empty;
}

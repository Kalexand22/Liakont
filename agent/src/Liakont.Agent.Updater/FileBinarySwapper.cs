namespace Liakont.Agent.Updater;

using System.IO;

/// <summary>
/// Remplacement réel des binaires par copie de fichiers (ADR-0013). L'updater tourne depuis un dossier
/// SÉPARÉ : le service étant arrêté, les binaires du dossier d'installation ne sont plus chargés et
/// peuvent être écrasés. La sauvegarde conserve l'ancienne version pour le rollback.
/// </summary>
public sealed class FileBinarySwapper : IBinarySwapper
{
    /// <inheritdoc/>
    public void Backup(string installDirectory, string backupDirectory)
    {
        if (Directory.Exists(installDirectory))
        {
            CopyDirectory(installDirectory, backupDirectory);
        }
    }

    /// <inheritdoc/>
    public void Apply(string stagingDirectory, string installDirectory)
    {
        CopyDirectory(stagingDirectory, installDirectory);
    }

    /// <inheritdoc/>
    public void Restore(string backupDirectory, string installDirectory)
    {
        CopyDirectory(backupDirectory, installDirectory);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
        }
    }
}

namespace Liakont.Agent.Core.Heartbeat;

using System;
using System.IO;

/// <summary>
/// Sonde d'espace disque réelle : lit l'espace disponible du volume contenant un chemin donné (la file
/// locale <c>C:\ProgramData\Liakont\</c>, F12 §2.3). BEST-EFFORT : toute erreur (volume réseau
/// indisponible, chemin invalide, accès refusé) renvoie <c>null</c> — la mesure de disque ne doit
/// jamais faire échouer un heartbeat (F12 §2.5).
/// </summary>
public sealed class DriveDiskFreeSpaceProbe : IDiskFreeSpaceProbe
{
    private readonly string _path;

    /// <summary>Crée une sonde sur le volume contenant <paramref name="path"/>.</summary>
    /// <param name="path">Un chemin sur le volume à surveiller (typiquement le fichier de la file locale).</param>
    public DriveDiskFreeSpaceProbe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin à surveiller est requis.", nameof(path));
        }

        _path = path;
    }

    /// <inheritdoc />
    public long? GetAvailableFreeBytes()
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(_path));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root!);
            return drive.IsReady ? drive.AvailableFreeSpace : (long?)null;
        }
        catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is NotSupportedException)
        {
            // Mesure indisponible : best-effort, on n'interrompt pas le heartbeat.
            return null;
        }
    }
}

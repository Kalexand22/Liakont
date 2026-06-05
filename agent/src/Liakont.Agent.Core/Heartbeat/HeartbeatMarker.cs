namespace Liakont.Agent.Core.Heartbeat;

using System;
using System.Globalization;
using System.IO;

/// <summary>
/// Marqueur de heartbeat local (AGT04, ADR-0013) : l'agent le RAFRAÎCHIT à chaque cycle de heartbeat
/// sain et au démarrage. L'updater détaché surveille sa fraîcheur pour juger qu'une NOUVELLE version a
/// bien redémarré (sinon rollback). Best-effort : ne lève jamais (un échec d'écriture ne doit pas tuer
/// le thread de fond de l'agent, F12 §2.5).
/// </summary>
public sealed class HeartbeatMarker
{
    private readonly string _markerPath;

    /// <summary>Crée un marqueur de heartbeat sur le fichier indiqué.</summary>
    /// <param name="markerPath">Chemin du fichier marqueur.</param>
    public HeartbeatMarker(string markerPath)
    {
        _markerPath = markerPath;
    }

    /// <summary>Rafraîchit le marqueur (horodatage UTC). Ne lève jamais.</summary>
    public void Touch()
    {
        if (string.IsNullOrEmpty(_markerPath))
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_markerPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_markerPath, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
            // Best-effort.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

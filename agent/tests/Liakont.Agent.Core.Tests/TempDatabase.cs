namespace Liakont.Agent.Core.Tests;

using System;
using System.IO;

/// <summary>Fichier de base SQLite temporaire, nettoyé (avec ses sidecars -wal/-shm) en fin de test.</summary>
internal sealed class TempDatabase : IDisposable
{
    public TempDatabase()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "liakont-agent-tests",
            Guid.NewGuid().ToString("N") + ".db");
    }

    public string Path { get; }

    public void Dispose()
    {
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                string file = Path + suffix;
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // Nettoyage best-effort : un sidecar encore verrouillé sera supprimé par l'OS.
            }
        }
    }
}

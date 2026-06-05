namespace Liakont.Agent.Core.Tests;

using System;
using System.IO;

/// <summary>Répertoire temporaire unique, nettoyé récursivement en fin de test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "liakont-agent-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string relative) => System.IO.Path.Combine(Path, relative);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Nettoyage best-effort : un fichier encore verrouillé sera supprimé par l'OS.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

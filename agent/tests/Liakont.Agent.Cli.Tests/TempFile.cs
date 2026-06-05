namespace Liakont.Agent.Cli.Tests;

using System;
using System.IO;

/// <summary>Fichier temporaire jetable pour les tests (ex. un <c>agent.json</c> de test), supprimé au Dispose.</summary>
internal sealed class TempFile : IDisposable
{
    private TempFile(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TempFile WithContent(string content)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, content);
        return new TempFile(path);
    }

    public static string NonExistentPath() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (IOException)
        {
            // Best-effort : un fichier temporaire résiduel n'est pas un échec de test.
        }
    }
}

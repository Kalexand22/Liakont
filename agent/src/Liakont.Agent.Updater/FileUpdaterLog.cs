namespace Liakont.Agent.Updater;

using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>Journal fichier de l'updater (messages français horodatés). Best-effort : ne lève jamais.</summary>
public sealed class FileUpdaterLog : IUpdaterLog
{
    private readonly string _logPath;

    /// <summary>Crée un journal fichier.</summary>
    /// <param name="logPath">Chemin du fichier de log.</param>
    public FileUpdaterLog(string logPath)
    {
        _logPath = logPath;
    }

    /// <inheritdoc/>
    public void Write(string message)
    {
        if (string.IsNullOrEmpty(_logPath))
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string line = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffZ", CultureInfo.InvariantCulture) + " [UPDATER] " + message + Environment.NewLine;
            File.AppendAllText(_logPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (IOException)
        {
            // Journalisation best-effort : un échec d'écriture ne doit pas interrompre la mise à jour.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

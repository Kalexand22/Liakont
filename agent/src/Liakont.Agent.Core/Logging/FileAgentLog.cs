namespace Liakont.Agent.Core.Logging;

using System;
using System.Globalization;
using System.IO;
using System.Text;
using Liakont.Agent.Core.Time;

/// <summary>
/// Journal fichiers de l'agent : un fichier par jour (<c>yyyy-MM-dd_agent.log</c>) dans le répertoire
/// fourni, lignes horodatées et lisibles, rotation à <see cref="RetentionDays"/> jours. Thread-safe
/// (service + écritures concurrentes). L'horloge est injectée pour rendre datation et rotation testables.
/// </summary>
public sealed class FileAgentLog : IAgentLog
{
    /// <summary>Rétention des fichiers de journal, en jours (F12 §2).</summary>
    public const int RetentionDays = 90;

    private readonly string _directory;
    private readonly IClock _clock;
    private readonly object _sync = new object();

    public FileAgentLog(string directory, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Le répertoire de journal est requis.", nameof(directory));
        }

        _directory = directory;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public void Info(string message) => Write("INFO", message, exception: null);

    /// <inheritdoc />
    public void Warn(string message) => Write("WARN", message, exception: null);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        DateTime now = _clock.UtcNow;
        var line = new StringBuilder();
        line.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append("Z [")
            .Append(level)
            .Append("] ")
            .Append(message);
        if (exception != null)
        {
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        }

        lock (_sync)
        {
            Directory.CreateDirectory(_directory);
            string file = Path.Combine(_directory, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "_agent.log");
            File.AppendAllText(file, line.ToString() + Environment.NewLine, Encoding.UTF8);
            PurgeOldFiles(now);
        }
    }

    private void PurgeOldFiles(DateTime now)
    {
        DateTime cutoff = now.AddDays(-RetentionDays);
        foreach (string file in Directory.EnumerateFiles(_directory, "*_agent.log"))
        {
            string name = Path.GetFileName(file);
            string datePart = name.Length >= 10 ? name.Substring(0, 10) : string.Empty;
            if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDate)
                && fileDate < cutoff.Date)
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Fichier verrouillé (autre processus) : il sera purgé au prochain passage.
                }
            }
        }
    }
}

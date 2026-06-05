namespace Liakont.Agent.Core.Tests.Logging;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Core.Logging;
using Xunit;

/// <summary>
/// Journal fichiers (F12 §2) : un fichier daté par jour, lignes horodatées et lisibles, rotation au
/// delà de 90 jours. Horloge injectée → datation et rotation testées de façon déterministe.
/// </summary>
public sealed class FileAgentLogTests : IDisposable
{
    private readonly string _directory;

    public FileAgentLogTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "liakont-agent-log-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort
        }
    }

    [Fact]
    public void Info_writes_a_dated_file_with_the_message()
    {
        var clock = new MutableClock(new DateTime(2026, 6, 5, 14, 30, 0, DateTimeKind.Utc));
        var log = new FileAgentLog(_directory, clock);

        log.Info("Service démarré.");

        string expected = Path.Combine(_directory, "2026-06-05_agent.log");
        File.Exists(expected).Should().BeTrue();
        string content = File.ReadAllText(expected);
        content.Should().Contain("[INFO]").And.Contain("Service démarré.");
        content.Should().Contain("2026-06-05 14:30:00");
    }

    [Fact]
    public void Error_includes_exception_type_and_message()
    {
        var clock = new MutableClock(new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc));
        var log = new FileAgentLog(_directory, clock);

        log.Error("Échec du cycle.", new InvalidOperationException("boum"));

        string content = File.ReadAllText(Path.Combine(_directory, "2026-06-05_agent.log"));
        content.Should().Contain("[ERROR]").And.Contain("InvalidOperationException").And.Contain("boum");
    }

    [Fact]
    public void Old_log_files_are_purged_beyond_retention()
    {
        var clock = new MutableClock(new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc));
        Directory.CreateDirectory(_directory);

        string oldFile = Path.Combine(_directory, "2026-02-01_agent.log"); // > 90 jours avant le 2026-06-05
        string recentFile = Path.Combine(_directory, "2026-05-30_agent.log"); // < 90 jours
        File.WriteAllText(oldFile, "ancien\r\n");
        File.WriteAllText(recentFile, "récent\r\n");

        var log = new FileAgentLog(_directory, clock);
        log.Info("nouvelle entrée");

        File.Exists(oldFile).Should().BeFalse("un journal de plus de 90 jours doit être purgé");
        File.Exists(recentFile).Should().BeTrue("un journal de moins de 90 jours est conservé");
        File.Exists(Path.Combine(_directory, "2026-06-05_agent.log")).Should().BeTrue();
    }
}

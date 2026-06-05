namespace Liakont.Agent.Core.Tests.Update;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Core.Update;
using Xunit;

/// <summary>
/// Store de statut d'auto-update (fichier JSON partagé agent ↔ updater, ADR-0013). Best-effort :
/// jamais de levée ; un fichier absent ou corrompu se lit comme « pas de statut ».
/// </summary>
public class AutoUpdateStateStoreTests
{
    private static readonly DateTime Now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_recorded_status_is_read_back()
    {
        using (var workspace = new TempDirectory())
        {
            var store = new AutoUpdateStateStore(workspace.Combine("update-status.json"));
            store.Record(new AutoUpdateStatus("2.0.0", "Launched", true, "Mise à jour lancée.", Now));

            AutoUpdateStatus? read = store.TryGetLatest();

            read.Should().NotBeNull();
            read!.TargetVersion.Should().Be("2.0.0");
            read.Phase.Should().Be("Launched");
            read.Succeeded.Should().BeTrue();
            read.Message.Should().Be("Mise à jour lancée.");
            read.AtUtc.Should().Be(Now);
        }
    }

    [Fact]
    public void An_absent_status_file_reads_as_null()
    {
        using (var workspace = new TempDirectory())
        {
            var store = new AutoUpdateStateStore(workspace.Combine("update-status.json"));

            store.TryGetLatest().Should().BeNull();
        }
    }

    [Fact]
    public void A_corrupt_status_file_reads_as_null()
    {
        using (var workspace = new TempDirectory())
        {
            string path = workspace.Combine("update-status.json");
            File.WriteAllText(path, "{ ceci n'est pas du JSON valide");
            var store = new AutoUpdateStateStore(path);

            store.TryGetLatest().Should().BeNull();
        }
    }
}

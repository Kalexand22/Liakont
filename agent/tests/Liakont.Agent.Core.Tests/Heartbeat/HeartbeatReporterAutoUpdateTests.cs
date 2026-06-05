namespace Liakont.Agent.Core.Tests.Heartbeat;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Heartbeat;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Tests.Update;
using Liakont.Agent.Core.Transport;
using Liakont.Agent.Core.Update;
using Xunit;

/// <summary>
/// Câblage du heartbeat avec l'auto-update (AGT04) : une configuration porteuse d'une mise à jour est
/// TRANSMISE au service d'auto-update (déclencheur), et un échec d'auto-update remonte au heartbeat
/// via <c>LastError</c> (signalement, F12 §2.5). Couture optionnelle : sans service, rien ne change.
/// </summary>
public class HeartbeatReporterAutoUpdateTests
{
    private static readonly DateTime Now = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_configuration_carrying_an_update_is_forwarded_to_the_auto_update_service()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            var autoUpdate = new FakeAutoUpdateService();
            var client = new FakePlatformClient
            {
                OnSendHeartbeat = _ => new HeartbeatOutcome(
                    PlatformResponseKind.Ok,
                    new AgentConfigurationDto(updateRequired: true, updateUrl: "https://updates.example/m.json", versionManifestSignature: "c2ln"),
                    Now),
            };
            HeartbeatReporter reporter = CreateReporter(queue, client, autoUpdate);

            reporter.SendHeartbeat();

            autoUpdate.ConsideredConfigurations.Should().ContainSingle();
            autoUpdate.ConsideredConfigurations[0].UpdateRequired.Should().BeTrue();
        }
    }

    [Fact]
    public void A_recent_auto_update_failure_is_surfaced_in_the_heartbeat_last_error()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            var autoUpdate = new FakeAutoUpdateService
            {
                LatestStatus = new AutoUpdateStatus("2.0.0", "RejectedSignature", succeeded: false, "Mise à jour refusée : signature du manifeste invalide.", Now),
            };
            var client = new FakePlatformClient();
            HeartbeatReporter reporter = CreateReporter(queue, client, autoUpdate);

            reporter.SendHeartbeat();

            client.Heartbeats.Should().ContainSingle();
            client.Heartbeats[0].LastError.Should().Be("Mise à jour refusée : signature du manifeste invalide.");
        }
    }

    [Fact]
    public void Each_heartbeat_refreshes_the_local_marker_watched_by_the_updater()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        using (var workspace = new TempDirectory())
        {
            string markerPath = workspace.Combine("heartbeat.marker");
            var reporter = new HeartbeatReporter(
                new FakePlatformClient(),
                queue,
                new AgentRunJournal(queue),
                new FakeDiskFreeSpaceProbe(4096L),
                new PlatformConfigurationStore(queue),
                new MutableClock(Now),
                new CapturingAgentLog(),
                agentVersion: "1.0.0",
                heartbeatMarker: new HeartbeatMarker(markerPath));

            reporter.SendHeartbeat();

            System.IO.File.Exists(markerPath).Should().BeTrue("l'updater s'appuie sur la fraîcheur de ce marqueur pour confirmer un redémarrage sain");
        }
    }

    private static HeartbeatReporter CreateReporter(LocalQueue queue, FakePlatformClient client, FakeAutoUpdateService autoUpdate) =>
        new HeartbeatReporter(
            client,
            queue,
            new AgentRunJournal(queue),
            new FakeDiskFreeSpaceProbe(4096L),
            new PlatformConfigurationStore(queue),
            new MutableClock(Now),
            new CapturingAgentLog(),
            agentVersion: "1.0.0",
            autoUpdate: autoUpdate);
}

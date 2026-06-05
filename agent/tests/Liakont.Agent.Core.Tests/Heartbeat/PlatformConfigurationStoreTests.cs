namespace Liakont.Agent.Core.Tests.Heartbeat;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Heartbeat;
using Liakont.Agent.Core.Storage;
using Xunit;

/// <summary>
/// Store de la dernière configuration plateforme reçue (AGT03), persisté dans <c>agent_state</c>.
/// Vérifie le round-trip, l'écrasement, et la TOLÉRANCE à une valeur absente ou corrompue (repli local
/// sans exception — F12 §2.5).
/// </summary>
public class PlatformConfigurationStoreTests
{
    private static readonly DateTime Now = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Absent_configuration_returns_null()
    {
        WithStore((store, _) => store.TryGet().Should().BeNull());
    }

    [Fact]
    public void Saves_and_reads_back_the_configuration()
    {
        WithStore((store, _) =>
        {
            store.Save(new AgentConfigurationDto(
                extractionSchedule: "0 2 * * *",
                extractFromUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                latestAgentVersion: "1.4.0",
                updateRequired: true,
                updateUrl: "https://maj.test/agent.msi"));

            AgentConfigurationDto? read = store.TryGet();

            read.Should().NotBeNull();
            read!.ExtractionSchedule.Should().Be("0 2 * * *");
            read.ExtractFromUtc.Should().Be(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            read.LatestAgentVersion.Should().Be("1.4.0");
            read.UpdateRequired.Should().BeTrue();
            read.UpdateUrl.Should().Be("https://maj.test/agent.msi");
        });
    }

    [Fact]
    public void The_latest_save_overwrites_the_previous_one()
    {
        WithStore((store, _) =>
        {
            store.Save(new AgentConfigurationDto(extractionSchedule: "0 2 * * *"));
            store.Save(new AgentConfigurationDto(extractionSchedule: "0 5 * * *"));

            store.TryGet()!.ExtractionSchedule.Should().Be("0 5 * * *");
        });
    }

    [Fact]
    public void A_corrupt_stored_value_is_ignored_and_returns_null()
    {
        WithStore((store, queue) =>
        {
            queue.SetState(LocalQueue.LastConfigurationKey, "{ ceci n'est pas du json");

            store.TryGet().Should().BeNull("une config illisible ne doit pas empêcher l'agent de démarrer (repli local)");
        });
    }

    private static void WithStore(Action<PlatformConfigurationStore, LocalQueue> test)
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            test(new PlatformConfigurationStore(queue), queue);
        }
    }
}

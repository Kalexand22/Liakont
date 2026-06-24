namespace Liakont.Agent.Core.Tests.Hosting;

using System;
using System.Threading;
using FluentAssertions;
using Liakont.Agent.Core.Heartbeat;
using Liakont.Agent.Core.Hosting;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Tests.Heartbeat;
using Xunit;

/// <summary>
/// CONTRAT de la boucle de heartbeat (AGT03 §1, F12 §2.5 — régression RB16 « heartbeat muet au 1er
/// démarrage »). L'hôte (<c>AgentHost</c>) câble un <see cref="AgentBackgroundRunner"/> dédié au
/// heartbeat dont le délégué est exactement <c>_ =&gt; reporter.SendHeartbeat()</c>, à cadence propre et
/// indépendant du cycle d'extraction. Ce test exerce CE délégué via la MÊME primitive d'hôte, et garantit
/// le comportement attendu : un battement émis DÈS LE DÉMARRAGE (sans intervention) puis PÉRIODIQUEMENT,
/// sans aucun run d'extraction (« même hors run ») — c'est le seul canal qui rafraîchit « Dernier contact »
/// côté plateforme.
/// <para>
/// Il ne réinstancie PAS <c>AgentHost</c> (racine de composition liée à <c>ServiceBase</c> net48,
/// historiquement couverte en recette comme le reste du déployeur de service) : l'assemblage exact de
/// <c>AgentHost</c> (appel de <c>Start</c>/<c>Stop</c> du runner heartbeat, cadence
/// <c>HeartbeatInterval</c>) reste validé par la recette. Ce test verrouille le contrat du délégué que
/// l'hôte branche.
/// </para>
/// </summary>
public class AgentHeartbeatLoopTests
{
    private static readonly DateTime Now = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Heartbeat_loop_emits_at_startup_and_periodically_without_any_extraction_run()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            var client = new FakePlatformClient();
            HeartbeatReporter reporter = CreateReporter(queue, client);

            // Wiring IDENTIQUE à AgentHost : le délégué de l'hôte de fond se contente d'émettre le heartbeat.
            // Cadence courte pour le test ; aucun extracteur n'est branché → la boucle prouve « même hors run ».
            var runner = new AgentBackgroundRunner(_ => reporter.SendHeartbeat(), TimeSpan.FromMilliseconds(50), new NullAgentLog());
            try
            {
                runner.Start();

                // Au moins deux battements (1 au démarrage + ≥1 périodique) sans qu'aucune extraction ait lieu.
                SpinWait.SpinUntil(() => client.Heartbeats.Count >= 2, TimeSpan.FromSeconds(5))
                    .Should().BeTrue("la boucle doit émettre un heartbeat au démarrage puis périodiquement");
            }
            finally
            {
                runner.Stop(TimeSpan.FromSeconds(2));
                runner.Dispose();
            }

            client.Heartbeats.Count.Should().BeGreaterThanOrEqualTo(2);
            client.Heartbeats[0].ServiceState.Should().Be("Running");

            // « Même hors run » : aucun document n'a été poussé — le heartbeat ne dépend pas d'une extraction.
            client.PushedBatches.Should().BeEmpty();
        }
    }

    private static HeartbeatReporter CreateReporter(LocalQueue queue, FakePlatformClient client) =>
        new HeartbeatReporter(
            client,
            queue,
            new AgentRunJournal(queue),
            new FakeDiskFreeSpaceProbe(4096L),
            new PlatformConfigurationStore(queue),
            new MutableClock(Now),
            new NullAgentLog(),
            agentVersion: "1.2.3");
}

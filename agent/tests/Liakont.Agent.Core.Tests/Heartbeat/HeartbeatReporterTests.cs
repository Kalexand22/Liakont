namespace Liakont.Agent.Core.Tests.Heartbeat;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Heartbeat;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Transport;
using Xunit;

/// <summary>
/// Remontée d'état + pilotage centralisé (AGT03, F12 §2.5/§3.2). Couvre : heartbeat COMPLET (file,
/// erreurs, dernier run, sync, disque), application de la configuration plateforme (surcharge du
/// fichier local), échec SILENCIEUX (WARN, jamais d'exception, repli local), et démarrage avec/sans
/// réseau.
/// </summary>
public class HeartbeatReporterTests
{
    private static readonly DateTime Now = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly string[] LocalSchedule = { "03:00" };

    [Fact]
    public void Heartbeat_carries_the_full_local_state()
    {
        WithReporter((reporter, queue, journal, store, client, log) =>
        {
            EnqueueDocuments(queue, "A", "B", "C");
            MarkFirstAsError(queue);
            journal.RecordRunStarted(Now.AddMinutes(-10));
            journal.RecordRunFinished(Now.AddMinutes(-9), "Success");
            journal.RecordSuccessfulSync(Now.AddMinutes(-8));

            reporter.SendHeartbeat();

            HeartbeatRequestDto sent = client.Heartbeats.Should().ContainSingle().Subject;
            sent.AgentVersion.Should().Be("1.2.3");
            sent.ServiceState.Should().Be("Running");
            sent.PushQueueDepth.Should().Be(3);
            sent.PushQueueErrorCount.Should().Be(1);
            sent.LastRunOutcome.Should().Be("Success");
            sent.LastRunStartedUtc.Should().Be(Now.AddMinutes(-10));
            sent.LastSuccessfulSyncUtc.Should().Be(Now.AddMinutes(-8));
            sent.DiskFreeBytes.Should().Be(4096L);
        });
    }

    [Fact]
    public void A_platform_schedule_in_the_response_overrides_the_local_file()
    {
        WithReporter((reporter, queue, journal, store, client, log) =>
        {
            client.OnSendHeartbeat = _ => new HeartbeatOutcome(
                PlatformResponseKind.Ok,
                new AgentConfigurationDto(extractionSchedule: "0 2 * * *"),
                Now);

            HeartbeatOutcome outcome = reporter.SendHeartbeat();

            outcome.Kind.Should().Be(PlatformResponseKind.Ok);
            store.TryGet()!.ExtractionSchedule.Should().Be("0 2 * * *");
            reporter.ResolveEffectivePlan(LocalConfig()).IsPlatformControlled.Should().BeTrue();
        });
    }

    [Fact]
    public void An_unreachable_platform_logs_a_warning_keeps_local_config_and_never_throws()
    {
        WithReporter((reporter, queue, journal, store, client, log) =>
        {
            client.OnSendHeartbeat = _ => new HeartbeatOutcome(PlatformResponseKind.TransportError, reason: "réseau coupé");

            Func<HeartbeatOutcome> act = () => reporter.SendHeartbeat();

            HeartbeatOutcome outcome = act.Should().NotThrow().Subject;
            outcome.Kind.Should().Be(PlatformResponseKind.TransportError);
            log.Warnings.Should().NotBeEmpty();
            log.Warnings[0].Should().Contain("configuration locale");
            store.TryGet().Should().BeNull("aucune config plateforme appliquée → l'agent garde la sienne");

            // Sans config plateforme mémorisée, l'agent extrait selon son fichier local.
            reporter.ResolveEffectivePlan(LocalConfig()).IsPlatformControlled.Should().BeFalse();
        });
    }

    [Fact]
    public void Startup_loads_the_platform_configuration_when_reachable()
    {
        WithReporter((reporter, queue, journal, store, client, log) =>
        {
            client.OnGetConfiguration = () => new ConfigurationOutcome(
                PlatformResponseKind.Ok,
                new AgentConfigurationDto(extractionSchedule: "0 4 * * *"));

            ConfigurationOutcome outcome = reporter.LoadStartupConfiguration();

            client.ConfigurationReads.Should().Be(1);
            outcome.Kind.Should().Be(PlatformResponseKind.Ok);
            store.TryGet()!.ExtractionSchedule.Should().Be("0 4 * * *");
        });
    }

    [Fact]
    public void Startup_without_network_falls_back_to_the_local_configuration()
    {
        WithReporter((reporter, queue, journal, store, client, log) =>
        {
            client.OnGetConfiguration = () => new ConfigurationOutcome(PlatformResponseKind.TransportError, reason: "DNS");

            Func<ConfigurationOutcome> act = () => reporter.LoadStartupConfiguration();

            ConfigurationOutcome outcome = act.Should().NotThrow().Subject;
            outcome.Kind.Should().Be(PlatformResponseKind.TransportError);
            log.Warnings.Should().NotBeEmpty();
            store.TryGet().Should().BeNull();

            // L'agent démarre quand même : plan 100 % local.
            reporter.ResolveEffectivePlan(LocalConfig()).ScheduleSource.Should().Be(ExtractionScheduleSource.Local);
        });
    }

    [Fact]
    public void A_200_without_configuration_keeps_local_settings()
    {
        WithReporter((reporter, queue, journal, store, client, log) =>
        {
            client.OnSendHeartbeat = _ => new HeartbeatOutcome(PlatformResponseKind.Ok, configuration: null, serverTimeUtc: Now);

            reporter.SendHeartbeat();

            store.TryGet().Should().BeNull();
            log.Infos.Should().NotBeEmpty();
        });
    }

    private static ExtractionConfig LocalConfig() => new ExtractionConfig(
        adapter: "Fixture",
        odbcConnectionStringProtected: null,
        pdfPoolPath: null,
        schedule: LocalSchedule,
        catchUpOnStart: true);

    private static void EnqueueDocuments(LocalQueue queue, params string[] references)
    {
        foreach (string reference in references)
        {
            queue.Enqueue(QueueItem.ForDocument(reference, "h-" + reference, "{}"));
        }
    }

    private static void MarkFirstAsError(LocalQueue queue)
    {
        QueuedItem first = queue.PeekPending(1)[0];
        queue.MarkError(first.Id, "échec simulé");
    }

    private static void WithReporter(
        Action<HeartbeatReporter, LocalQueue, AgentRunJournal, PlatformConfigurationStore, FakePlatformClient, CapturingAgentLog> test)
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            var journal = new AgentRunJournal(queue);
            var store = new PlatformConfigurationStore(queue);
            var client = new FakePlatformClient();
            var log = new CapturingAgentLog();
            var reporter = new HeartbeatReporter(
                client,
                queue,
                journal,
                new FakeDiskFreeSpaceProbe(4096L),
                store,
                new MutableClock(Now),
                log,
                agentVersion: "1.2.3");

            test(reporter, queue, journal, store, client, log);
        }
    }
}

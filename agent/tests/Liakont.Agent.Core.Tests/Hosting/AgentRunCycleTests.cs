namespace Liakont.Agent.Core.Tests.Hosting;

using System;
using System.Threading;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Heartbeat;
using Liakont.Agent.Core.Hosting;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Tests.Update;
using Liakont.Agent.Core.Transport;
using Xunit;

/// <summary>
/// Cycle complet de l'agent (F12 §2.2) : extraction (sur la fenêtre [filigrane, maintenant[) puis
/// drainage. Un échec d'extraction typé (R7) n'empêche pas le drainage des éléments déjà en file.
/// </summary>
public class AgentRunCycleTests
{
    private static readonly DateTime Now = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Run_extracts_the_window_then_drains_to_the_platform()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.SetExtractionWatermarkUtc(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var extractor = new FixtureExtractor(
                "Fixture",
                documents: new[] { PivotTestData.Document("REF-1", new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc)) });
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(
                    PlatformResponseKind.Ok,
                    new[] { new DocumentPushResultDto("REF-1", DocumentPushStatus.Accepted) }),
            };
            AgentRunCycle cycle = CreateCycle(extractor, queue, client);

            cycle.Run(CancellationToken.None);

            client.PushedBatches.Should().ContainSingle();
            queue.Peek(QueueItemStatus.InProgress, QueueItemKind.Document, 10).Should().ContainSingle();
        }
    }

    [Fact]
    public void Run_still_drains_when_extraction_fails_with_a_retryable_error()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.SetExtractionWatermarkUtc(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            queue.Enqueue(QueueItem.ForDocument("PREV-1", "h-prev", "{}")); // élément d'un run précédent
            var extractor = new ThrowingExtractor(() => new SourceUnavailableException("ODBC coupé"));
            var log = new CapturingAgentLog();
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(
                    PlatformResponseKind.Ok,
                    new[] { new DocumentPushResultDto("PREV-1", DocumentPushStatus.Accepted) }),
            };
            AgentRunCycle cycle = CreateCycle(extractor, queue, client, log);

            cycle.Run(CancellationToken.None);

            log.Warnings.Should().NotBeEmpty();
            client.PushedBatches.Should().ContainSingle(); // le drainage a bien eu lieu malgré l'échec d'extraction
        }
    }

    [Fact]
    public void Run_records_last_run_and_successful_sync_in_the_journal()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.SetExtractionWatermarkUtc(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var extractor = new FixtureExtractor(
                "Fixture",
                documents: new[] { PivotTestData.Document("REF-1", new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc)) });
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(
                    PlatformResponseKind.Ok,
                    new[] { new DocumentPushResultDto("REF-1", DocumentPushStatus.Accepted) }),
            };
            var journal = new AgentRunJournal(queue);
            AgentRunCycle cycle = CreateCycle(extractor, queue, client, journal: journal);

            cycle.Run(CancellationToken.None);

            journal.LastRunStartedUtc.Should().Be(Now);
            journal.LastRunCompletedUtc.Should().Be(Now);
            journal.LastRunOutcome.Should().Be("Success");

            // Un document accepté (« en cours », ACK 2 temps) compte comme un push abouti → sync.
            journal.LastSuccessfulSyncUtc.Should().Be(Now);
        }
    }

    [Fact]
    public void Run_records_a_failed_extraction_outcome_and_no_sync_when_nothing_is_pushed()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            var extractor = new ThrowingExtractor(() => new SourceUnavailableException("ODBC coupé"));
            var client = new FakePlatformClient();
            var journal = new AgentRunJournal(queue);
            AgentRunCycle cycle = CreateCycle(extractor, queue, client, journal: journal);

            cycle.Run(CancellationToken.None);

            journal.LastRunOutcome.Should().Be("SourceUnavailable");
            journal.LastError.Should().Contain("ODBC coupé");
            journal.LastSuccessfulSyncUtc.Should().BeNull("aucun push abouti → pas de synchronisation");
        }
    }

    [Fact]
    public void Run_records_drain_incomplete_when_extraction_succeeds_but_the_push_is_rejected()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.SetExtractionWatermarkUtc(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var extractor = new FixtureExtractor(
                "Fixture",
                documents: new[] { PivotTestData.Document("REF-1", new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc)) });
            var client = new FakePlatformClient
            {
                // 401 arrête le drainage immédiatement (StoppedBy = Unauthorized, sans backoff).
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(PlatformResponseKind.Unauthorized),
            };
            var journal = new AgentRunJournal(queue);
            AgentRunCycle cycle = CreateCycle(extractor, queue, client, journal: journal);

            cycle.Run(CancellationToken.None);

            journal.LastRunOutcome.Should().Be("DrainIncomplete:Unauthorized");
            journal.LastError.Should().NotBeNullOrEmpty();
            journal.LastSuccessfulSyncUtc.Should().BeNull("aucun push abouti → pas de synchronisation");
        }
    }

    [Fact]
    public void Run_records_a_failed_extraction_but_still_syncs_when_the_backlog_drains()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.Enqueue(QueueItem.ForDocument("PREV-1", "h-prev", "{}")); // backlog d'un run précédent
            var extractor = new ThrowingExtractor(() => new SourceUnavailableException("ODBC coupé"));
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(
                    PlatformResponseKind.Ok,
                    new[] { new DocumentPushResultDto("PREV-1", DocumentPushStatus.Accepted) }),
            };
            var journal = new AgentRunJournal(queue);
            AgentRunCycle cycle = CreateCycle(extractor, queue, client, journal: journal);

            cycle.Run(CancellationToken.None);

            // L'issue reflète l'échec d'extraction, mais le backlog a bien été poussé → sync enregistrée.
            journal.LastRunOutcome.Should().Be("SourceUnavailable");
            journal.LastSuccessfulSyncUtc.Should().Be(Now);
        }
    }

    [Fact]
    public void Run_signals_the_auto_update_service_on_a_426_push()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.SetExtractionWatermarkUtc(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var extractor = new FixtureExtractor(
                "Fixture",
                documents: new[] { PivotTestData.Document("REF-1", new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc)) });
            var client = new FakePlatformClient
            {
                // 426 : version d'agent non supportée → le cycle signale le besoin d'auto-update (AGT04).
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(PlatformResponseKind.UpgradeRequired),
            };
            var autoUpdate = new FakeAutoUpdateService();
            var log = new CapturingAgentLog();
            var extractionCycle = new ExtractionCycle(queue, log);
            var drainer = new QueueDrainer(queue, client, log, new ExponentialBackoff(), _ => { });
            var cycle = new AgentRunCycle(extractor, extractionCycle, drainer, queue, new MutableClock(Now), log, journal: null, autoUpdate: autoUpdate);

            cycle.Run(CancellationToken.None);

            autoUpdate.PushUpgradeSignals.Should().Be(1);
        }
    }

    private static AgentRunCycle CreateCycle(
        Liakont.Agent.Core.IExtractor extractor,
        LocalQueue queue,
        FakePlatformClient client,
        CapturingAgentLog? log = null,
        AgentRunJournal? journal = null)
    {
        CapturingAgentLog effectiveLog = log ?? new CapturingAgentLog();
        var extractionCycle = new ExtractionCycle(queue, effectiveLog);
        var drainer = new QueueDrainer(queue, client, effectiveLog, new ExponentialBackoff(), _ => { });
        return new AgentRunCycle(extractor, extractionCycle, drainer, queue, new MutableClock(Now), effectiveLog, journal);
    }
}

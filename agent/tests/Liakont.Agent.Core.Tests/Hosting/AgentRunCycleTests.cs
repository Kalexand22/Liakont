namespace Liakont.Agent.Core.Tests.Hosting;

using System;
using System.Threading;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Hosting;
using Liakont.Agent.Core.Storage;
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

    private static AgentRunCycle CreateCycle(
        Liakont.Agent.Core.IExtractor extractor,
        LocalQueue queue,
        FakePlatformClient client,
        CapturingAgentLog? log = null)
    {
        CapturingAgentLog effectiveLog = log ?? new CapturingAgentLog();
        var extractionCycle = new ExtractionCycle(queue, effectiveLog);
        var drainer = new QueueDrainer(queue, client, effectiveLog, new ExponentialBackoff(), _ => { });
        return new AgentRunCycle(extractor, extractionCycle, drainer, queue, new MutableClock(Now), effectiveLog);
    }
}

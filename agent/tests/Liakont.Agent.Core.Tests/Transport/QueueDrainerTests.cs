namespace Liakont.Agent.Core.Tests.Transport;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Transport;
using Newtonsoft.Json;
using Xunit;

/// <summary>
/// Drainage de la file (ADR-0012 + F12 §3.3) : acquittement en DEUX temps, réconciliation par statut,
/// batching, re-découpe 413, backoff 429/5xx/réseau (pas de retry 400), reprise après coupure sans
/// perte, et idempotence. Tests RÉELS contre une base SQLite temporaire + couture de plateforme mockée.
/// </summary>
public class QueueDrainerTests
{
    private static readonly DateTime Now = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Accepted_push_marks_in_progress_and_never_purges()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueDocument(queue, "REF-1", "h1");
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => Ok("REF-1", DocumentPushStatus.Accepted),
            };
            QueueDrainer drainer = Drainer(queue, client, new NullAgentLog());

            DrainResult result = drainer.DrainOnce();

            result.DocumentsInProgress.Should().Be(1);
            queue.Peek(QueueItemStatus.InProgress, QueueItemKind.Document, 10).Should().ContainSingle();
            queue.IsAlreadyPushed(QueueItemKind.Document, "REF-1", "h1").Should().BeFalse();
        }
    }

    [Fact]
    public void Reconcile_processed_acknowledges_and_purges()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueInProgress(queue, "REF-1", "h1");
            var client = new FakePlatformClient
            {
                OnGetStatus = (sref, hash) => new DocumentStatusOutcome(PlatformResponseKind.Ok, DocumentIntakeStatus.Processed),
            };
            QueueDrainer drainer = Drainer(queue, client, new NullAgentLog());

            DrainResult result = drainer.DrainOnce();

            result.DocumentsAcknowledged.Should().Be(1);
            queue.Count().Should().Be(0);
            queue.IsAlreadyPushed(QueueItemKind.Document, "REF-1", "h1").Should().BeTrue();
        }
    }

    [Fact]
    public void Reconcile_rejected_purges_and_signals_the_operator()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueInProgress(queue, "REF-1", "h1");
            var log = new CapturingAgentLog();
            var client = new FakePlatformClient
            {
                OnGetStatus = (sref, hash) => new DocumentStatusOutcome(PlatformResponseKind.Ok, DocumentIntakeStatus.Rejected, "non conforme"),
            };
            QueueDrainer drainer = Drainer(queue, client, log);

            DrainResult result = drainer.DrainOnce();

            result.DocumentsRejected.Should().Be(1);
            queue.Count().Should().Be(0);
            log.Warnings.Should().ContainSingle(w => w.Contains("REF-1"));
        }
    }

    [Fact]
    public void Reconcile_pending_resends_a_received_but_unranged_document_without_loss()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueInProgress(queue, "REF-1", "h1");
            var client = new FakePlatformClient
            {
                // Intake échoué côté plateforme : reçu mais non rangé.
                OnGetStatus = (sref, hash) => new DocumentStatusOutcome(PlatformResponseKind.Ok, DocumentIntakeStatus.Pending),
                OnPushDocuments = (docs, regimes) => Ok("REF-1", DocumentPushStatus.Accepted),
            };
            QueueDrainer drainer = Drainer(queue, client, new NullAgentLog());

            DrainResult result = drainer.DrainOnce();

            result.DocumentsResent.Should().Be(1);
            result.DocumentsInProgress.Should().Be(1); // renvoyé puis re-poussé
            client.PushedBatches.Should().ContainSingle();
            queue.Count().Should().Be(1); // jamais perdu
        }
    }

    [Fact]
    public void Push_time_rejection_purges_and_signals_without_re_push()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueDocument(queue, "REF-1", "h1");
            var log = new CapturingAgentLog();
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => Ok("REF-1", DocumentPushStatus.Rejected, "non conforme"),
            };
            QueueDrainer drainer = Drainer(queue, client, log);

            DrainResult result = drainer.DrainOnce();

            result.DocumentsRejected.Should().Be(1);
            queue.Count().Should().Be(0);
            queue.IsAlreadyPushed(QueueItemKind.Document, "REF-1", "h1").Should().BeTrue();
            log.Warnings.Should().ContainSingle(w => w.Contains("REF-1"));
        }
    }

    [Fact]
    public void Bad_request_marks_error_and_does_not_retry()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueDocument(queue, "REF-1", "h1");
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(PlatformResponseKind.BadRequest, reason: "HTTP 400"),
            };
            QueueDrainer drainer = Drainer(queue, client, new NullAgentLog());

            drainer.DrainOnce();
            drainer.DrainOnce(); // un second cycle ne doit PAS re-pousser un 400

            queue.Peek(QueueItemStatus.Error, QueueItemKind.Document, 10).Should().ContainSingle();
            client.PushedBatches.Should().ContainSingle();
        }
    }

    [Fact]
    public void Payload_too_large_resplits_the_batch()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueDocument(queue, "REF-1", "h1");
            EnqueueDocument(queue, "REF-2", "h2");
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => docs.Count > 1
                    ? new PushBatchOutcome(PlatformResponseKind.PayloadTooLarge)
                    : new PushBatchOutcome(PlatformResponseKind.Ok),
            };
            QueueDrainer drainer = Drainer(queue, client, new NullAgentLog());

            DrainResult result = drainer.DrainOnce();

            client.PushedBatches.Select(b => b.Documents.Count).Should().Equal(2, 1, 1);
            result.DocumentsInProgress.Should().Be(2);
            queue.Peek(QueueItemStatus.InProgress, QueueItemKind.Document, 10).Should().HaveCount(2);
        }
    }

    [Fact]
    public void Single_oversized_document_is_errored()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueDocument(queue, "REF-1", "h1");
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(PlatformResponseKind.PayloadTooLarge),
            };
            QueueDrainer drainer = Drainer(queue, client, new NullAgentLog());

            DrainResult result = drainer.DrainOnce();

            result.DocumentsErrored.Should().Be(1);
            queue.Peek(QueueItemStatus.Error, QueueItemKind.Document, 10).Should().ContainSingle();
        }
    }

    [Fact]
    public void Throttling_retries_with_exponential_backoff_and_keeps_items_in_queue()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueDocument(queue, "REF-1", "h1");
            var waits = new List<TimeSpan>();
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(PlatformResponseKind.Throttled),
            };
            QueueDrainer drainer = Drainer(queue, client, new NullAgentLog(), waits.Add, maxTransientAttempts: 3);

            DrainResult result = drainer.DrainOnce();

            client.PushedBatches.Should().HaveCount(3);
            waits.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
            result.StoppedBy.Should().Be(PlatformResponseKind.Throttled);
            queue.Peek(QueueItemStatus.Pending, QueueItemKind.Document, 10).Should().ContainSingle(); // rien perdu
        }
    }

    [Fact]
    public void Network_outage_then_recovery_loses_nothing()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueDocument(queue, "REF-1", "h1");
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(PlatformResponseKind.TransportError),
            };
            QueueDrainer drainer = Drainer(queue, client, new NullAgentLog());

            drainer.DrainOnce(); // coupure : tout reste en file
            queue.Peek(QueueItemStatus.Pending, QueueItemKind.Document, 10).Should().ContainSingle();

            client.OnPushDocuments = (docs, regimes) => Ok("REF-1", DocumentPushStatus.Accepted);
            DrainResult recovered = drainer.DrainOnce(); // réseau revenu : rattrapage

            recovered.DocumentsInProgress.Should().Be(1);
            queue.Peek(QueueItemStatus.InProgress, QueueItemKind.Document, 10).Should().ContainSingle();
        }
    }

    [Fact]
    public void Unauthorized_stops_the_drain_immediately_without_loss()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueDocument(queue, "REF-1", "h1");
            var log = new CapturingAgentLog();
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => new PushBatchOutcome(PlatformResponseKind.Unauthorized),
            };
            QueueDrainer drainer = Drainer(queue, client, log);

            DrainResult result = drainer.DrainOnce();

            result.StoppedBy.Should().Be(PlatformResponseKind.Unauthorized);
            queue.Peek(QueueItemStatus.Pending, QueueItemKind.Document, 10).Should().ContainSingle();
            log.Errors.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void Source_tax_regimes_are_attached_to_the_push()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueDocument(queue, "REF-1", "h1");
            queue.SetState(LocalQueue.SourceTaxRegimesKey, JsonConvert.SerializeObject(new[] { new SourceTaxRegimeDto("0", "Normal", 2) }));
            var client = new FakePlatformClient
            {
                OnPushDocuments = (docs, regimes) => Ok("REF-1", DocumentPushStatus.Accepted),
            };
            QueueDrainer drainer = Drainer(queue, client, new NullAgentLog());

            drainer.DrainOnce();

            client.PushedBatches.Should().ContainSingle();
            client.PushedBatches[0].SourceTaxRegimes.Should().ContainSingle().Which.Code.Should().Be("0");
        }
    }

    [Fact]
    public void Pdf_push_success_acknowledges_the_file()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.Enqueue(QueueItem.ForPdf(QueueItemKind.Pdf, "REF-1", "C:\\pdf\\a.pdf", "a.pdf"));
            var client = new FakePlatformClient
            {
                OnPushLinkedPdf = (sref, path) => new PdfPushOutcome(PlatformResponseKind.Ok),
            };
            QueueDrainer drainer = Drainer(queue, client, new NullAgentLog());

            DrainResult result = drainer.DrainOnce();

            result.PdfsAcknowledged.Should().Be(1);
            queue.Count().Should().Be(0);
            client.LinkedPdfPushes.Should().ContainSingle();
        }
    }

    private static PushBatchOutcome Ok(string sourceReference, DocumentPushStatus status, string? reason = null)
    {
        return new PushBatchOutcome(
            PlatformResponseKind.Ok,
            new[] { new DocumentPushResultDto(sourceReference, status, reason) });
    }

    private static void EnqueueDocument(LocalQueue queue, string sourceReference, string hash)
    {
        queue.Enqueue(QueueItem.ForDocument(sourceReference, hash, "{}"));
    }

    private static void EnqueueInProgress(LocalQueue queue, string sourceReference, string hash)
    {
        queue.Enqueue(QueueItem.ForDocument(sourceReference, hash, "{}"));
        long id = queue.Peek(QueueItemStatus.Pending, QueueItemKind.Document, 100)
            .First(q => q.SourceReference == sourceReference)
            .Id;
        queue.MarkInProgress(id);
    }

    private static QueueDrainer Drainer(
        LocalQueue queue,
        FakePlatformClient client,
        IAgentLog log,
        Action<TimeSpan>? wait = null,
        int maxTransientAttempts = 3)
    {
        return new QueueDrainer(
            queue,
            client,
            log,
            new ExponentialBackoff(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(5)),
            wait ?? (_ => { }),
            QueueDrainer.DefaultMaxBatchSize,
            maxTransientAttempts);
    }
}

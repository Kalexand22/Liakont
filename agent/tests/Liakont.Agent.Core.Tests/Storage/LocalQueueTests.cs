namespace Liakont.Agent.Core.Tests.Storage;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Core.Storage;
using Xunit;

/// <summary>
/// File locale SQLite (F12 §2.3) : mode WAL, enfilage idempotent, acquittement (push_queue →
/// pushed_log), purge de pushed_log au-delà de 90 jours SANS toucher à push_queue/agent_state, et
/// état (filigrane d'extraction). Tests RÉELS contre une base SQLite temporaire (pas de mock).
/// </summary>
public class LocalQueueTests
{
    private static readonly DateTime Now = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Database_uses_WAL_journal_mode()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.GetJournalMode().Should().BeEquivalentTo("wal");
        }
    }

    [Fact]
    public void Enqueue_then_peek_returns_the_pending_document()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            EnqueueResult result = queue.Enqueue(QueueItem.ForDocument("FAC-001", "hash-001", "{\"n\":1}"));

            result.Should().Be(EnqueueResult.Enqueued);
            queue.Count().Should().Be(1);

            IReadOnlyList<QueuedItem> pending = queue.PeekPending(10);
            pending.Should().HaveCount(1);
            pending[0].SourceReference.Should().Be("FAC-001");
            pending[0].PayloadHash.Should().Be("hash-001");
            pending[0].PayloadJson.Should().Be("{\"n\":1}");
            pending[0].Status.Should().Be(QueueItemStatus.Pending);
            pending[0].Kind.Should().Be(QueueItemKind.Document);
        }
    }

    [Fact]
    public void Enqueue_is_idempotent_on_same_key()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.Enqueue(QueueItem.ForDocument("FAC-001", "hash-001", "{}")).Should().Be(EnqueueResult.Enqueued);
            queue.Enqueue(QueueItem.ForDocument("FAC-001", "hash-001", "{}")).Should().Be(EnqueueResult.AlreadyQueued);

            queue.Count().Should().Be(1);
        }
    }

    [Fact]
    public void Different_hash_for_same_reference_is_a_new_item()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.Enqueue(QueueItem.ForDocument("FAC-001", "hash-001", "{}"));
            queue.Enqueue(QueueItem.ForDocument("FAC-001", "hash-002", "{}"));

            queue.Count().Should().Be(2);
        }
    }

    [Fact]
    public void Acknowledge_moves_item_to_pushed_log_and_removes_it_from_the_queue()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.Enqueue(QueueItem.ForDocument("FAC-001", "hash-001", "{}"));
            long id = queue.PeekPending(1)[0].Id;

            queue.Acknowledge(id);

            queue.Count().Should().Be(0);
            queue.IsAlreadyPushed(QueueItemKind.Document, "FAC-001", "hash-001").Should().BeTrue();
        }
    }

    [Fact]
    public void MarkInProgress_excludes_item_from_peek_pending()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.Enqueue(QueueItem.ForDocument("FAC-001", "hash-001", "{}"));
            long id = queue.PeekPending(1)[0].Id;

            queue.MarkInProgress(id);

            queue.PeekPending(10).Should().BeEmpty();
            queue.Count().Should().Be(1, "« en cours » ne quitte pas la file (ADR-0012)");
        }
    }

    [Fact]
    public void MarkError_increments_attempts_and_keeps_item_retryable()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.Enqueue(QueueItem.ForDocument("FAC-001", "hash-001", "{}"));
            long id = queue.PeekPending(1)[0].Id;

            queue.MarkError(id, "réseau indisponible");

            QueuedItem item = queue.PeekPending(10)[0];
            item.Status.Should().Be(QueueItemStatus.Error);
            item.Attempts.Should().Be(1);
            item.LastError.Should().Be("réseau indisponible");
        }
    }

    [Fact]
    public void PurgeExpiredPushedLog_removes_old_entries_but_not_recent_ones()
    {
        var clock = new MutableClock(Now);
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, clock))
        {
            // Acquittement « ancien » (sera au-delà de 90 jours après avancée d'horloge).
            queue.Enqueue(QueueItem.ForDocument("FAC-OLD", "hash-old", "{}"));
            queue.Acknowledge(queue.PeekPending(1)[0].Id);

            clock.Advance(TimeSpan.FromDays(91));

            // Acquittement « récent ».
            queue.Enqueue(QueueItem.ForDocument("FAC-NEW", "hash-new", "{}"));
            queue.Acknowledge(queue.PeekPending(1)[0].Id);

            int purged = queue.PurgeExpiredPushedLog();

            purged.Should().Be(1);
            queue.IsAlreadyPushed(QueueItemKind.Document, "FAC-OLD", "hash-old").Should().BeFalse();
            queue.IsAlreadyPushed(QueueItemKind.Document, "FAC-NEW", "hash-new").Should().BeTrue();
        }
    }

    [Fact]
    public void PurgeExpiredPushedLog_never_touches_push_queue_or_agent_state()
    {
        var clock = new MutableClock(Now);
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, clock))
        {
            queue.Enqueue(QueueItem.ForDocument("FAC-PENDING", "hash-p", "{}"));
            queue.SetState("clef", "valeur");

            clock.Advance(TimeSpan.FromDays(365));
            queue.PurgeExpiredPushedLog();

            queue.Count().Should().Be(1, "push_queue n'est jamais purgée automatiquement");
            queue.GetState("clef").Should().Be("valeur", "agent_state n'est jamais purgé automatiquement");
        }
    }

    [Fact]
    public void Agent_state_round_trips_and_watermark_is_persisted()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            queue.GetState("absente").Should().BeNull();

            queue.SetState("config", "{\"x\":1}");
            queue.GetState("config").Should().Be("{\"x\":1}");

            queue.SetState("config", "{\"x\":2}");
            queue.GetState("config").Should().Be("{\"x\":2}", "SetState remplace la valeur existante");

            var watermark = new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc);
            queue.SetExtractionWatermarkUtc(watermark);
            queue.GetExtractionWatermarkUtc().Should().Be(watermark);
        }
    }

    [Fact]
    public void State_persists_across_reopen()
    {
        using (var db = new TempDatabase())
        {
            using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
            {
                queue.Enqueue(QueueItem.ForDocument("FAC-001", "hash-001", "{}"));
                queue.SetState("k", "v");
            }

            using (var reopened = new LocalQueue(db.Path, new MutableClock(Now)))
            {
                reopened.Count().Should().Be(1);
                reopened.GetState("k").Should().Be("v");
            }
        }
    }
}

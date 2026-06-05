namespace Liakont.Agent.Cli.Tests.Diagnostics;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Cli.Diagnostics;
using Liakont.Agent.Cli.Tests;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Time;
using Xunit;

/// <summary>
/// Lecture réelle de la file locale (SQLite) par <see cref="LocalQueueSnapshotReader"/> : les comptages
/// par statut sont exacts et les éléments à traiter (en attente + en erreur) sont listés.
/// </summary>
public class LocalQueueSnapshotReaderTests
{
    [Fact]
    public void Reads_accurate_counts_and_actionable_items()
    {
        using var db = new TempDatabase();

        IReadOnlyList<QueuedItem> queued;
        using (var queue = new LocalQueue(db.Path, new SystemClock()))
        {
            queue.Enqueue(QueueItem.ForDocument("FAC-1", "h1", "{}"));
            queue.Enqueue(QueueItem.ForDocument("FAC-2", "h2", "{}"));
            queue.Enqueue(QueueItem.ForDocument("FAC-3", "h3", "{}"));
            queue.Enqueue(QueueItem.ForDocument("FAC-4", "h4", "{}"));

            queued = queue.PeekPending(10);
            queue.MarkError(queued[0].Id, "délai dépassé");
            queue.MarkInProgress(queued[1].Id);
        }

        QueueSnapshot snapshot = LocalQueueSnapshotReader.Read(db.Path);

        snapshot.Total.Should().Be(4);
        snapshot.Pending.Should().Be(2);
        snapshot.InProgress.Should().Be(1);
        snapshot.Error.Should().Be(1);

        // PeekPending ne renvoie que les éléments à pousser (en attente + en erreur) : 3 ici.
        snapshot.Items.Should().HaveCount(3);
    }

    [Fact]
    public void Empty_queue_reports_zero()
    {
        using var db = new TempDatabase();

        QueueSnapshot snapshot = LocalQueueSnapshotReader.Read(db.Path);

        snapshot.Total.Should().Be(0);
        snapshot.Pending.Should().Be(0);
        snapshot.InProgress.Should().Be(0);
        snapshot.Error.Should().Be(0);
        snapshot.Items.Should().BeEmpty();
    }
}

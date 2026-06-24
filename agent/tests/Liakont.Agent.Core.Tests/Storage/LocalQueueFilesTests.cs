namespace Liakont.Agent.Core.Tests.Storage;

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Core.Storage;
using Xunit;

/// <summary>
/// Purge de l'état LOCAL de l'agent (BUG-2) : la file SQLite <c>agent-queue.db</c> — qui porte le
/// filigrane d'extraction (<see cref="LocalQueue.ExtractionWatermarkKey"/>, table <c>agent_state</c>) —
/// et ses annexes WAL <c>-wal</c>/<c>-shm</c> doivent disparaître à la désinstallation pour qu'une
/// réinstallation reparte d'un état vierge. Tests RÉELS contre des fichiers temporaires (pas de mock).
/// </summary>
public class LocalQueueFilesTests
{
    private static readonly DateTime Now = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Enumerate_returns_the_database_and_its_wal_sidecars()
    {
        using (var db = new TempDatabase())
        {
            IReadOnlyList<string> paths = LocalQueueFiles.Enumerate(db.Path);

            paths.Should().ContainInOrder(db.Path, db.Path + "-wal", db.Path + "-shm");
            paths.Should().HaveCount(3);
        }
    }

    [Fact]
    public void Enumerate_rejects_a_blank_path()
    {
        Action call = () => LocalQueueFiles.Enumerate("   ");

        call.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Purge_removes_the_database_carrying_the_watermark_and_its_sidecars()
    {
        using (var db = new TempDatabase())
        {
            // Crée une VRAIE file avec un filigrane d'extraction posé (état qui survivait à BUG-2),
            // puis libère la connexion avant la purge (le service est arrêté à la désinstallation).
            using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
            {
                queue.SetExtractionWatermarkUtc(Now);
                queue.GetExtractionWatermarkUtc().Should().Be(Now);
            }

            File.Exists(db.Path).Should().BeTrue("la base doit exister avant la purge");

            int removed = LocalQueueFiles.Purge(db.Path);

            removed.Should().BeGreaterThan(0);
            File.Exists(db.Path).Should().BeFalse("la base locale doit être purgée");
            File.Exists(db.Path + "-wal").Should().BeFalse();
            File.Exists(db.Path + "-shm").Should().BeFalse();
        }
    }

    [Fact]
    public void Purge_reinstall_starts_from_a_blank_watermark()
    {
        using (var db = new TempDatabase())
        {
            using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
            {
                queue.SetExtractionWatermarkUtc(Now);
            }

            LocalQueueFiles.Purge(db.Path);

            // Réinstallation simulée : la base recréée repart d'un filigrane vide.
            using (var reinstalled = new LocalQueue(db.Path, new MutableClock(Now)))
            {
                reinstalled.GetExtractionWatermarkUtc().Should().BeNull(
                    "une réinstallation doit repartir d'un état vierge (BUG-2)");
            }
        }
    }

    [Fact]
    public void Purge_is_idempotent_when_nothing_is_present()
    {
        using (var dir = new TempDirectory())
        {
            string absent = dir.Combine("agent-queue.db");

            int removed = LocalQueueFiles.Purge(absent);

            removed.Should().Be(0);
        }
    }
}

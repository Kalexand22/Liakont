namespace Liakont.Agent.Core.Tests.Heartbeat;

using System;
using FluentAssertions;
using Liakont.Agent.Core.Heartbeat;
using Liakont.Agent.Core.Storage;
using Xunit;

/// <summary>
/// Journal du dernier run / dernière sync (AGT03), persisté dans <c>agent_state</c>. Vérifie le
/// round-trip UTC, l'écrasement (état COURANT, pas une piste d'audit) et l'effacement de l'erreur par
/// un run sain.
/// </summary>
public class AgentRunJournalTests
{
    private static readonly DateTime Now = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Empty_journal_returns_nulls()
    {
        WithJournal((journal, _) =>
        {
            journal.LastRunStartedUtc.Should().BeNull();
            journal.LastRunCompletedUtc.Should().BeNull();
            journal.LastRunOutcome.Should().BeNull();
            journal.LastError.Should().BeNull();
            journal.LastSuccessfulSyncUtc.Should().BeNull();
        });
    }

    [Fact]
    public void Records_run_started_finished_and_sync_with_utc_roundtrip()
    {
        WithJournal((journal, _) =>
        {
            journal.RecordRunStarted(Now);
            journal.RecordRunFinished(Now.AddMinutes(2), "Success");
            journal.RecordSuccessfulSync(Now.AddMinutes(1));

            journal.LastRunStartedUtc.Should().Be(Now);
            journal.LastRunStartedUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
            journal.LastRunCompletedUtc.Should().Be(Now.AddMinutes(2));
            journal.LastRunOutcome.Should().Be("Success");
            journal.LastSuccessfulSyncUtc.Should().Be(Now.AddMinutes(1));
        });
    }

    [Fact]
    public void A_failed_run_records_its_error_then_a_healthy_run_clears_it()
    {
        WithJournal((journal, _) =>
        {
            journal.RecordRunFinished(Now, "SourceUnavailable", "ODBC injoignable");
            journal.LastError.Should().Be("ODBC injoignable");
            journal.LastRunOutcome.Should().Be("SourceUnavailable");

            journal.RecordRunFinished(Now.AddHours(1), "Success");
            journal.LastError.Should().BeNull("un run sain efface la dernière erreur (santé courante)");
            journal.LastRunOutcome.Should().Be("Success");
        });
    }

    [Fact]
    public void Record_run_finished_rejects_a_blank_outcome()
    {
        WithJournal((journal, _) =>
        {
            Action act = () => journal.RecordRunFinished(Now, "  ");
            act.Should().Throw<ArgumentException>();
        });
    }

    private static void WithJournal(Action<AgentRunJournal, LocalQueue> test)
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Now)))
        {
            test(new AgentRunJournal(queue), queue);
        }
    }
}

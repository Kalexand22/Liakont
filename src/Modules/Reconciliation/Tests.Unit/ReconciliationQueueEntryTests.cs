namespace Liakont.Modules.Reconciliation.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Reconciliation.Domain;
using Xunit;

public sealed class ReconciliationQueueEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid DocId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void AutoReconciled_SetsResolvedAndHighConfidence()
    {
        var entry = ReconciliationQueueEntry.AutoReconciled("p1", "FAC.pdf", DocId, MatchStrategy.FileName, "motif", Now);

        entry.Status.Should().Be(ReconciliationStatus.ReconciledAuto);
        entry.ProposedDocumentId.Should().Be(DocId);
        entry.Strategy.Should().Be(MatchStrategy.FileName);
        entry.Confidence.Should().Be(MatchConfidence.High);
        entry.ResolvedUtc.Should().Be(Now);
        entry.OperatorIdentity.Should().BeNull();
    }

    [Fact]
    public void PendingProposal_IsMediumAndUnresolved()
    {
        var entry = ReconciliationQueueEntry.PendingProposal("p1", "doc.pdf", DocId, "motif", Now);

        entry.Status.Should().Be(ReconciliationStatus.PendingManual);
        entry.Confidence.Should().Be(MatchConfidence.Medium);
        entry.Strategy.Should().Be(MatchStrategy.DateAndAmount);
        entry.ResolvedUtc.Should().BeNull();
    }

    [Fact]
    public void Orphan_HasNoDocumentNorStrategy()
    {
        var entry = ReconciliationQueueEntry.Orphan("p1", "scan.pdf", "aucune correspondance", Now);

        entry.Status.Should().Be(ReconciliationStatus.Orphan);
        entry.ProposedDocumentId.Should().BeNull();
        entry.Strategy.Should().BeNull();
        entry.Confidence.Should().BeNull();
    }

    [Fact]
    public void ConfirmManually_OnOrphan_TransitionsToReconciledManual()
    {
        var entry = ReconciliationQueueEntry.Orphan("p1", "scan.pdf", "orphelin", Now);

        entry.ConfirmManually(DocId, "operateur.test", "rattachement manuel", Now.AddHours(1));

        entry.Status.Should().Be(ReconciliationStatus.ReconciledManual);
        entry.ProposedDocumentId.Should().Be(DocId);
        entry.OperatorIdentity.Should().Be("operateur.test");
        entry.ResolvedUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void ConfirmManually_OnAlreadyReconciled_Throws()
    {
        var entry = ReconciliationQueueEntry.AutoReconciled("p1", "FAC.pdf", DocId, MatchStrategy.FileName, "auto", Now);

        Action act = () => entry.ConfirmManually(DocId, "operateur.test", "motif", Now.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ConfirmManually_WithoutOperator_Throws()
    {
        var entry = ReconciliationQueueEntry.PendingProposal("p1", "doc.pdf", DocId, "motif", Now);

        Action act = () => entry.ConfirmManually(DocId, "  ", "motif", Now);

        act.Should().Throw<ArgumentException>();
    }
}

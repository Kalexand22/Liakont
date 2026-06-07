namespace Liakont.Modules.Pipeline.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain;
using Xunit;

/// <summary>Invariants de l'agrégat <see cref="RunLog"/> (INV-PIPELINE-006).</summary>
public sealed class RunLogTests
{
    private static readonly DateTimeOffset Start0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_Opens_An_Incomplete_Run_With_Zero_Counters()
    {
        var run = RunLog.Start(PipelineRunType.Check, PipelineRunTrigger.Scheduled, Start0);

        run.IsCompleted.Should().BeFalse();
        run.CompletedAt.Should().BeNull();
        run.RunType.Should().Be(PipelineRunType.Check);
        run.Trigger.Should().Be(PipelineRunTrigger.Scheduled);
        run.StartedAt.Should().Be(Start0);
        run.DocumentsProcessed.Should().Be(0);
        run.DocumentsSucceeded.Should().Be(0);
        run.DocumentsFailed.Should().Be(0);
        run.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Complete_Sets_End_And_Counters()
    {
        var run = RunLog.Start(PipelineRunType.Send, PipelineRunTrigger.Manual, Start0);

        run.Complete(Start0.AddMinutes(2), documentsProcessed: 5, documentsSucceeded: 4, documentsFailed: 1, detail: "ok");

        run.IsCompleted.Should().BeTrue();
        run.CompletedAt.Should().Be(Start0.AddMinutes(2));
        run.DocumentsProcessed.Should().Be(5);
        run.DocumentsSucceeded.Should().Be(4);
        run.DocumentsFailed.Should().Be(1);
        run.Detail.Should().Be("ok");
    }

    [Fact]
    public void Complete_Before_Start_Throws()
    {
        var run = RunLog.Start(PipelineRunType.Sync, PipelineRunTrigger.Scheduled, Start0);

        var act = () => run.Complete(Start0.AddMinutes(-1), 0, 0, 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Complete_With_Negative_Counter_Throws()
    {
        var run = RunLog.Start(PipelineRunType.Check, PipelineRunTrigger.Manual, Start0);

        var act = () => run.Complete(Start0.AddMinutes(1), documentsProcessed: -1, documentsSucceeded: 0, documentsFailed: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

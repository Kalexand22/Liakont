namespace Liakont.Host.Tests.Unit.Pipeline;

using System;
using FluentAssertions;
using Liakont.Host.Pipeline;
using Liakont.Modules.Pipeline.Contracts;
using Xunit;

public sealed class PipelineRunRowTests
{
    [Theory]
    [InlineData(PipelineRunType.Check, "Contrôle")]
    [InlineData(PipelineRunType.Send, "Envoi")]
    [InlineData(PipelineRunType.Sync, "Synchronisation")]
    [InlineData(PipelineRunType.Aggregate, "Agrégation")]
    [InlineData(PipelineRunType.Rectify, "Rectification")]
    public void RunTypeLabel_Maps_Every_Run_Type_To_A_French_Label(PipelineRunType runType, string expected)
    {
        PipelineRunRow.RunTypeLabel(runType).Should().Be(expected);
    }

    [Theory]
    [InlineData(PipelineRunTrigger.Manual, "Manuel")]
    [InlineData(PipelineRunTrigger.Scheduled, "Planifié")]
    [InlineData(PipelineRunTrigger.Event, "Ingestion")]
    public void TriggerLabel_Maps_Every_Trigger_To_A_French_Label(PipelineRunTrigger trigger, string expected)
    {
        PipelineRunRow.TriggerLabel(trigger).Should().Be(expected);
    }

    [Fact]
    public void FormatDuration_Returns_En_Cours_When_Run_Is_Not_Completed()
    {
        var started = new DateTimeOffset(2026, 6, 8, 2, 0, 0, TimeSpan.Zero);

        PipelineRunRow.FormatDuration(started, completedAt: null).Should().Be("En cours");
    }

    [Fact]
    public void FormatDuration_Formats_Seconds_Minutes_And_Hours_In_French()
    {
        var started = new DateTimeOffset(2026, 6, 8, 2, 0, 0, TimeSpan.Zero);

        PipelineRunRow.FormatDuration(started, started.AddSeconds(42)).Should().Be("42 s");
        PipelineRunRow.FormatDuration(started, started.AddMinutes(3).AddSeconds(7)).Should().Be("3 min 7 s");
        PipelineRunRow.FormatDuration(started, started.AddHours(1).AddMinutes(5)).Should().Be("1 h 5 min");
    }

    [Fact]
    public void FormatDuration_Clamps_A_Negative_Span_To_Zero()
    {
        var started = new DateTimeOffset(2026, 6, 8, 2, 0, 0, TimeSpan.Zero);

        // Dérive d'horloge entre deux nœuds : fin avant le début → 0 s plutôt qu'une durée absurde.
        PipelineRunRow.FormatDuration(started, started.AddSeconds(-30)).Should().Be("0 s");
    }

    [Fact]
    public void FromDto_Projects_Counters_Verbatim_And_Labels_In_French()
    {
        var started = new DateTimeOffset(2026, 6, 8, 1, 30, 0, TimeSpan.Zero);
        var dto = new PipelineRunLogDto
        {
            Id = Guid.NewGuid(),
            RunType = PipelineRunType.Send,
            Trigger = PipelineRunTrigger.Scheduled,
            StartedAt = started,
            CompletedAt = started.AddMinutes(2),
            DocumentsProcessed = 12,
            DocumentsSucceeded = 10,
            DocumentsFailed = 2,
            Detail = "envoyés: 10, rejetés: 2",
        };

        var row = PipelineRunRow.FromDto(dto);

        row.Id.Should().Be(dto.Id);
        row.StartedAt.Should().Be(started);
        row.Nature.Should().Be("Envoi");
        row.Trigger.Should().Be("Planifié");
        row.Duration.Should().Be("2 min 0 s");
        row.DocumentsProcessed.Should().Be(12);
        row.DocumentsValidated.Should().Be(10);
        row.DocumentsFailed.Should().Be(2);
        row.Detail.Should().Be("envoyés: 10, rejetés: 2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromDto_Renders_An_Em_Dash_When_Detail_Is_Absent(string? detail)
    {
        var started = new DateTimeOffset(2026, 6, 8, 1, 30, 0, TimeSpan.Zero);
        var dto = new PipelineRunLogDto
        {
            Id = Guid.NewGuid(),
            RunType = PipelineRunType.Check,
            Trigger = PipelineRunTrigger.Manual,
            StartedAt = started,
            CompletedAt = null,
            DocumentsProcessed = 0,
            DocumentsSucceeded = 0,
            DocumentsFailed = 0,
            Detail = detail,
        };

        var row = PipelineRunRow.FromDto(dto);

        row.Detail.Should().Be("—");
        row.Duration.Should().Be("En cours");
    }
}

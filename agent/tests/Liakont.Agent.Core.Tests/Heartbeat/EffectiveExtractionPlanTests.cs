namespace Liakont.Agent.Core.Tests.Heartbeat;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Heartbeat;
using Xunit;

/// <summary>
/// Résolution du plan d'extraction effectif (AGT03, F12 §6.1 décision n°3) : la planification
/// plateforme surcharge le fichier local QUAND elle est présente ; sinon le local gouverne ; la
/// fenêtre imposée vient de la plateforme ; le rattrapage reste local.
/// </summary>
public class EffectiveExtractionPlanTests
{
    private static readonly string[] LocalTimes = { "03:00", "15:00" };

    [Fact]
    public void Platform_schedule_overrides_the_local_file_when_present()
    {
        var platform = new AgentConfigurationDto(extractionSchedule: "0 2 * * *");

        EffectiveExtractionPlan plan = EffectiveExtractionPlan.Resolve(LocalConfig(), platform);

        plan.ScheduleSource.Should().Be(ExtractionScheduleSource.Platform);
        plan.IsPlatformControlled.Should().BeTrue();
        plan.PlatformSchedule.Should().Be("0 2 * * *");
    }

    [Fact]
    public void Without_platform_config_the_plan_is_fully_local()
    {
        EffectiveExtractionPlan plan = EffectiveExtractionPlan.Resolve(LocalConfig(), platform: null);

        plan.ScheduleSource.Should().Be(ExtractionScheduleSource.Local);
        plan.IsPlatformControlled.Should().BeFalse();
        plan.PlatformSchedule.Should().BeNull();
        plan.LocalSchedule.Should().BeEquivalentTo("03:00", "15:00");
        plan.ImposedFromUtc.Should().BeNull();
        plan.ImposedToUtc.Should().BeNull();
    }

    [Fact]
    public void An_imposed_window_is_taken_from_the_platform_even_without_a_platform_schedule()
    {
        var platform = new AgentConfigurationDto(
            extractionSchedule: null,
            extractFromUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            extractToUtc: new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc));

        EffectiveExtractionPlan plan = EffectiveExtractionPlan.Resolve(LocalConfig(), platform);

        // Pas de planification plateforme → le local gouverne la planification…
        plan.ScheduleSource.Should().Be(ExtractionScheduleSource.Local);

        // …mais la fenêtre imposée par la plateforme est bien portée.
        plan.ImposedFromUtc.Should().Be(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        plan.ImposedToUtc.Should().Be(new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Catch_up_on_start_stays_a_local_setting()
    {
        var platform = new AgentConfigurationDto(extractionSchedule: "0 2 * * *");

        EffectiveExtractionPlan.Resolve(LocalConfig(catchUp: true), platform).CatchUpOnStart.Should().BeTrue();
        EffectiveExtractionPlan.Resolve(LocalConfig(catchUp: false), platform).CatchUpOnStart.Should().BeFalse();
    }

    [Fact]
    public void A_blank_platform_schedule_does_not_override_the_local_file()
    {
        var platform = new AgentConfigurationDto(extractionSchedule: "   ");

        EffectiveExtractionPlan plan = EffectiveExtractionPlan.Resolve(LocalConfig(), platform);

        plan.ScheduleSource.Should().Be(ExtractionScheduleSource.Local);
    }

    private static ExtractionConfig LocalConfig(bool catchUp = true) => new ExtractionConfig(
        adapter: "Fixture",
        odbcConnectionStringProtected: null,
        pdfPoolPath: null,
        schedule: LocalTimes,
        catchUpOnStart: catchUp);
}

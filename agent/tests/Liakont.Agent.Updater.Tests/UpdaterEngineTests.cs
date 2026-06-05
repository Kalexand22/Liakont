namespace Liakont.Agent.Updater.Tests;

using System;
using FluentAssertions;
using Xunit;

/// <summary>
/// Moteur de remplacement de binaires avec rollback (AGT04, ADR-0013). Couvre : application nominale
/// quand le redémarrage est sain, ROLLBACK quand la nouvelle version ne redémarre pas sainement, et
/// ROLLBACK quand le remplacement lui-même échoue.
/// </summary>
public class UpdaterEngineTests
{
    [Fact]
    public void A_healthy_restart_applies_the_update_without_rollback()
    {
        var service = new FakeServiceControl();
        var swapper = new FakeBinarySwapper();
        var health = new FakeServiceHealthProbe { Healthy = true };
        var engine = new UpdaterEngine(service, swapper, health, new CapturingUpdaterLog());

        UpdaterResult result = engine.Run(Plan());

        result.Outcome.Should().Be(UpdaterOutcome.Applied);
        result.Succeeded.Should().BeTrue();
        swapper.Backed.Should().BeTrue();
        swapper.Applied.Should().BeTrue();
        swapper.Restored.Should().BeFalse("un redémarrage sain ne déclenche aucun rollback");
        service.StopCount.Should().Be(1);
        service.StartCount.Should().Be(1);
    }

    [Fact]
    public void An_unhealthy_restart_rolls_back_to_the_previous_version()
    {
        var service = new FakeServiceControl();
        var swapper = new FakeBinarySwapper();
        var health = new FakeServiceHealthProbe { Healthy = false };
        var engine = new UpdaterEngine(service, swapper, health, new CapturingUpdaterLog());

        UpdaterResult result = engine.Run(Plan());

        result.Outcome.Should().Be(UpdaterOutcome.RolledBack);
        swapper.Applied.Should().BeTrue("la nouvelle version a bien été posée avant l'échec de santé");
        swapper.Restored.Should().BeTrue("les anciens binaires sont restaurés");
        service.StartCount.Should().BeGreaterThan(1, "le service est redémarré sur l'ancienne version");
    }

    [Fact]
    public void A_failure_while_replacing_binaries_rolls_back()
    {
        var service = new FakeServiceControl();
        var swapper = new FakeBinarySwapper { ThrowOnApply = true };
        var health = new FakeServiceHealthProbe { Healthy = true };
        var engine = new UpdaterEngine(service, swapper, health, new CapturingUpdaterLog());

        UpdaterResult result = engine.Run(Plan());

        result.Outcome.Should().Be(UpdaterOutcome.RolledBack);
        swapper.Restored.Should().BeTrue("un échec de remplacement déclenche la restauration");
    }

    private static UpdaterPlan Plan() => new UpdaterPlan(
        targetVersion: "2.0.0",
        stagingDirectory: @"C:\work\staging",
        installDirectory: @"C:\Program Files\Liakont Agent",
        backupDirectory: @"C:\work\backup",
        serviceName: "LiakontAgent",
        healthTimeout: TimeSpan.FromSeconds(1),
        heartbeatMarkerPath: @"C:\ProgramData\Liakont\heartbeat.marker",
        statusPath: @"C:\ProgramData\Liakont\update-status.json");
}

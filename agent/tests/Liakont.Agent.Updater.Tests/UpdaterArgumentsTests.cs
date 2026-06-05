namespace Liakont.Agent.Updater.Tests;

using System;
using FluentAssertions;
using Xunit;

/// <summary>Analyse des arguments de l'updater (contrat ligne de commande agent ↔ updater, ADR-0013).</summary>
public class UpdaterArgumentsTests
{
    [Fact]
    public void A_full_argument_set_builds_a_plan()
    {
        string[] args = FullArgs();

        UpdaterArguments.TryParse(args, out UpdaterPlan? plan, out string? logPath, out string? error).Should().BeTrue();
        error.Should().BeNull();
        logPath.Should().Be(@"C:\ProgramData\Liakont\updater.log");
        plan!.TargetVersion.Should().Be("2.0.0");
        plan.InstallDirectory.Should().Be(@"C:\Program Files\Liakont Agent\");
        plan.ServiceName.Should().Be("LiakontAgent");
        plan.HealthTimeout.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void A_missing_required_argument_is_reported()
    {
        string[] args = { "--target-version", "2.0.0", "--staging", @"C:\s" };

        UpdaterArguments.TryParse(args, out UpdaterPlan? plan, out string? _, out string? error).Should().BeFalse();
        plan.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void An_absent_or_invalid_health_timeout_falls_back_to_the_default()
    {
        UpdaterArguments.TryParse(WithoutHealthTimeout(), out UpdaterPlan? plan, out string? _, out string? _).Should().BeTrue();
        plan!.HealthTimeout.Should().Be(TimeSpan.FromSeconds(300));
    }

    [Fact]
    public void Paths_with_spaces_and_a_trailing_backslash_are_preserved()
    {
        UpdaterArguments.TryParse(FullArgs(), out UpdaterPlan? plan, out string? _, out string? _).Should().BeTrue();
        plan!.InstallDirectory.Should().EndWith(@"Liakont Agent\");
        plan.StagingDirectory.Should().Be(@"C:\work\staging dir");
    }

    private static string[] FullArgs() => new[]
    {
        "--target-version", "2.0.0",
        "--staging", @"C:\work\staging dir",
        "--install", @"C:\Program Files\Liakont Agent\",
        "--backup", @"C:\work\backup",
        "--service", "LiakontAgent",
        "--health-timeout-seconds", "120",
        "--log", @"C:\ProgramData\Liakont\updater.log",
        "--status", @"C:\ProgramData\Liakont\update-status.json",
        "--heartbeat-marker", @"C:\ProgramData\Liakont\heartbeat.marker",
    };

    private static string[] WithoutHealthTimeout() => new[]
    {
        "--target-version", "2.0.0",
        "--staging", @"C:\s",
        "--install", @"C:\i",
        "--backup", @"C:\b",
        "--service", "LiakontAgent",
        "--status", @"C:\st.json",
        "--heartbeat-marker", @"C:\m",
    };
}

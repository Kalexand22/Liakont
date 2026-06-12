namespace Liakont.Modules.FleetSupervision.Tests.Unit;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Liakont.Modules.FleetSupervision.Contracts.DTOs;
using Xunit;

/// <summary>
/// Calcul pur des alertes de flotte (OPS04) : instance muette (seuil strict), sauvegarde en échec (absente
/// ou périmée), version obsolète (comparaison conservatrice). Une instance saine ne produit aucune alerte.
/// </summary>
public sealed class FleetAlertEvaluatorTests
{
    private const string Latest = "1.4.0";
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly FleetAlertThresholds Thresholds = new(InstanceMuteThresholdMinutes: 30, BackupMaxAgeHours: 26);

    [Fact]
    public void Healthy_Instance_Produces_No_Alert()
    {
        FleetInstanceDto instance = Healthy();

        var alerts = FleetAlertEvaluator.Evaluate(instance, Thresholds, Latest, Now);

        alerts.Should().BeEmpty();
    }

    [Fact]
    public void Mute_When_Last_Seen_Exceeds_Threshold()
    {
        FleetInstanceDto instance = Healthy() with { LastSeenUtc = Now.AddMinutes(-31) };

        var alerts = FleetAlertEvaluator.Evaluate(instance, Thresholds, Latest, Now);

        alerts.Should().ContainSingle(a => a.Kind == FleetAlertKind.InstanceMute)
            .Which.Severity.Should().Be(FleetAlertSeverity.Critical);
    }

    [Fact]
    public void Not_Mute_At_Exactly_The_Threshold()
    {
        FleetInstanceDto instance = Healthy() with { LastSeenUtc = Now.AddMinutes(-30) };

        var alerts = FleetAlertEvaluator.Evaluate(instance, Thresholds, Latest, Now);

        alerts.Should().NotContain(a => a.Kind == FleetAlertKind.InstanceMute);
    }

    [Fact]
    public void Backup_Failure_When_No_Backup_Known()
    {
        FleetInstanceDto instance = Healthy() with { LastSuccessfulBackupUtc = null };

        var alerts = FleetAlertEvaluator.Evaluate(instance, Thresholds, Latest, Now);

        alerts.Should().ContainSingle(a => a.Kind == FleetAlertKind.BackupFailure);
    }

    [Fact]
    public void Backup_Failure_When_Backup_Too_Old()
    {
        FleetInstanceDto instance = Healthy() with { LastSuccessfulBackupUtc = Now.AddHours(-27) };

        var alerts = FleetAlertEvaluator.Evaluate(instance, Thresholds, Latest, Now);

        alerts.Should().ContainSingle(a => a.Kind == FleetAlertKind.BackupFailure);
    }

    [Fact]
    public void No_Backup_Failure_When_Backup_Recent()
    {
        FleetInstanceDto instance = Healthy() with { LastSuccessfulBackupUtc = Now.AddHours(-25) };

        var alerts = FleetAlertEvaluator.Evaluate(instance, Thresholds, Latest, Now);

        alerts.Should().NotContain(a => a.Kind == FleetAlertKind.BackupFailure);
    }

    [Fact]
    public void Obsolete_When_Version_Behind_Latest()
    {
        FleetInstanceDto instance = Healthy() with { Version = "1.3.0" };

        var alerts = FleetAlertEvaluator.Evaluate(instance, Thresholds, Latest, Now);

        alerts.Should().ContainSingle(a => a.Kind == FleetAlertKind.ObsoleteVersion)
            .Which.Severity.Should().Be(FleetAlertSeverity.Warning);
    }

    [Fact]
    public void Not_Obsolete_When_Version_Current_Or_Newer()
    {
        FleetAlertEvaluator.Evaluate(Healthy() with { Version = "1.4.0" }, Thresholds, Latest, Now)
            .Should().NotContain(a => a.Kind == FleetAlertKind.ObsoleteVersion);
        FleetAlertEvaluator.Evaluate(Healthy() with { Version = "1.5.0" }, Thresholds, Latest, Now)
            .Should().NotContain(a => a.Kind == FleetAlertKind.ObsoleteVersion);
    }

    [Fact]
    public void Unparseable_Version_Does_Not_Raise_A_False_Obsolete_Alert()
    {
        FleetInstanceDto instance = Healthy() with { Version = "dev" };

        var alerts = FleetAlertEvaluator.Evaluate(instance, Thresholds, Latest, Now);

        alerts.Should().NotContain(a => a.Kind == FleetAlertKind.ObsoleteVersion);
    }

    [Fact]
    public void A_Failing_Instance_Can_Raise_Several_Alerts_At_Once()
    {
        FleetInstanceDto instance = Healthy() with
        {
            LastSeenUtc = Now.AddHours(-2),
            LastSuccessfulBackupUtc = null,
            Version = "1.0.0",
        };

        var alerts = FleetAlertEvaluator.Evaluate(instance, Thresholds, Latest, Now);

        alerts.Select(a => a.Kind).Should().BeEquivalentTo(
        [
            FleetAlertKind.InstanceMute,
            FleetAlertKind.BackupFailure,
            FleetAlertKind.ObsoleteVersion,
        ]);
    }

    [Fact]
    public void EvaluateAll_Concatenates_Per_Instance()
    {
        FleetInstanceDto healthy = Healthy() with { InstanceId = "ok" };
        FleetInstanceDto muteAndObsolete = Healthy() with
        {
            InstanceId = "ko",
            LastSeenUtc = Now.AddHours(-3),
            Version = "1.0.0",
        };

        var alerts = FleetAlertEvaluator.EvaluateAll([healthy, muteAndObsolete], Thresholds, Latest, Now);

        alerts.Should().HaveCount(2);
        alerts.Should().OnlyContain(a => a.InstanceId == "ko");
    }

    private static FleetInstanceDto Healthy() => new()
    {
        InstanceId = "inst-1",
        DisplayName = "Instance 1",
        HostingMode = InstanceHostingMode.Operated,
        Version = Latest,
        HostHealth = InstanceHealthStatus.Healthy,
        DatabaseHealth = InstanceHealthStatus.Healthy,
        KeycloakHealth = InstanceHealthStatus.Healthy,
        TenantCount = 3,
        DiskFreeBytes = 50_000_000_000,
        DiskTotalBytes = 100_000_000_000,
        LastSuccessfulBackupUtc = Now.AddHours(-2),
        ContactEmail = null,
        FirstSeenUtc = Now.AddDays(-10),
        LastSeenUtc = Now.AddMinutes(-5),
    };
}

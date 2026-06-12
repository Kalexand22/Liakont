namespace Liakont.Modules.FleetSupervision.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.FleetSupervision.Contracts;
using Liakont.Modules.FleetSupervision.Domain;
using Xunit;

/// <summary>
/// Entité d'instance de flotte (OPS04) : <see cref="FleetInstance.Register"/> valide l'identifiant, normalise
/// (repli du libellé, valeurs négatives bornées à zéro) et initialise premier-vu = dernier-vu = now.
/// </summary>
public sealed class FleetInstanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Register_Rejects_An_Empty_Instance_Id()
    {
        var report = new InstanceHeartbeatReport { InstanceId = "   ", Version = "1.0.0" };

        Action act = () => FleetInstance.Register(report, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Register_Falls_Back_DisplayName_To_InstanceId_And_Trims()
    {
        var report = new InstanceHeartbeatReport { InstanceId = "  inst-7 ", DisplayName = "  ", Version = " 1.2.3 " };

        FleetInstance instance = FleetInstance.Register(report, Now);

        instance.InstanceId.Should().Be("inst-7");
        instance.DisplayName.Should().Be("inst-7");
        instance.Version.Should().Be("1.2.3");
    }

    [Fact]
    public void Register_Clamps_Negative_Counts_And_Sizes_To_Zero()
    {
        var report = new InstanceHeartbeatReport
        {
            InstanceId = "inst-1",
            TenantCount = -5,
            DiskFreeBytes = -1,
            DiskTotalBytes = -2,
        };

        FleetInstance instance = FleetInstance.Register(report, Now);

        instance.TenantCount.Should().Be(0);
        instance.DiskFreeBytes.Should().Be(0);
        instance.DiskTotalBytes.Should().Be(0);
    }

    [Fact]
    public void Register_Initializes_First_And_Last_Seen_To_Now()
    {
        var report = new InstanceHeartbeatReport
        {
            InstanceId = "inst-1",
            HostingMode = InstanceHostingMode.SelfHosted,
            ContactEmail = " it@editeur.example ",
        };

        FleetInstance instance = FleetInstance.Register(report, Now);

        instance.FirstSeenUtc.Should().Be(Now);
        instance.LastSeenUtc.Should().Be(Now);
        instance.HostingMode.Should().Be(InstanceHostingMode.SelfHosted);
        instance.ContactEmail.Should().Be("it@editeur.example");
    }
}

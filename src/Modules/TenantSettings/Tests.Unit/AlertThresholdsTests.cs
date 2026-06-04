namespace Liakont.Modules.TenantSettings.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Xunit;

public sealed class AlertThresholdsTests
{
    [Fact]
    public void CreateDefault_Applies_F12A_Defaults()
    {
        var thresholds = AlertThresholds.CreateDefault(Guid.NewGuid());

        thresholds.AgentSilentHours.Should().Be(24);
        thresholds.MissedRunHours.Should().Be(36);
        thresholds.PushQueueMaxItems.Should().Be(50);
        thresholds.PushQueueMaxAgeHours.Should().Be(6);
        thresholds.BlockedDocumentsDays.Should().Be(5);
        thresholds.PaRejectionsDays.Should().Be(2);
        thresholds.AlertTenantContact.Should().BeFalse();
    }

    [Fact]
    public void Create_With_Non_Positive_Threshold_Throws()
    {
        var act = () => AlertThresholds.Create(Guid.NewGuid(), 0, 36, 50, 6, 5, 2, false);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-002*");
    }
}

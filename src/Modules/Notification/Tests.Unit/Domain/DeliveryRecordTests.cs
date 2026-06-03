namespace Stratum.Modules.Notification.Tests.Unit.Domain;

using FluentAssertions;
using Stratum.Modules.Notification.Domain.Entities;
using Xunit;

public class DeliveryRecordTests
{
    [Fact]
    public void Create_Should_Initialize_WithDefaults()
    {
        var record = DeliveryRecord.Create(
            "reservation-routing",
            "service@example.com",
            "reservation",
            "REQ-001",
            null);

        record.Id.Should().NotBeEmpty();
        record.TemplateCode.Should().Be("reservation-routing");
        record.RecipientEmail.Should().Be("service@example.com");
        record.EntityType.Should().Be("reservation");
        record.EntityId.Should().Be("REQ-001");
        record.RetryCount.Should().Be(0);
        record.SlaBreached.Should().BeFalse();
        record.DeliveredAt.Should().BeNull();
        record.FailedAt.Should().BeNull();
    }

    [Fact]
    public void Create_Should_Throw_When_TemplateCode_Empty()
    {
        var act = () => DeliveryRecord.Create(string.Empty, "email@test.com", null, null, null);
        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-031*");
    }

    [Fact]
    public void Create_Should_Throw_When_RecipientEmail_Empty()
    {
        var act = () => DeliveryRecord.Create("template", string.Empty, null, null, null);
        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-032*");
    }

    [Fact]
    public void MarkDelivered_Should_Set_DeliveredAt()
    {
        var record = DeliveryRecord.Create("tmpl", "email@test.com", null, null, null);

        record.MarkDelivered();

        record.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkFailed_Should_Set_FailedAt_And_Increment_RetryCount()
    {
        var record = DeliveryRecord.Create("tmpl", "email@test.com", null, null, null);

        record.MarkFailed();

        record.FailedAt.Should().NotBeNull();
        record.RetryCount.Should().Be(1);
    }

    [Fact]
    public void MarkFailed_Multiple_Times_Should_Increment_RetryCount()
    {
        var record = DeliveryRecord.Create("tmpl", "email@test.com", null, null, null);

        record.MarkFailed();
        record.MarkFailed();
        record.MarkFailed();

        record.RetryCount.Should().Be(3);
    }

    [Fact]
    public void MarkSlaBreached_Should_Set_Flag()
    {
        var record = DeliveryRecord.Create("tmpl", "email@test.com", null, null, null);

        record.MarkSlaBreached();

        record.SlaBreached.Should().BeTrue();
    }

    [Fact]
    public void ClearFailureForRetry_Should_Reset_FailedAt()
    {
        var record = DeliveryRecord.Create("tmpl", "email@test.com", null, null, null);
        record.MarkFailed();
        record.FailedAt.Should().NotBeNull();

        record.ClearFailureForRetry();

        record.FailedAt.Should().BeNull();
        record.RetryCount.Should().Be(1); // Count preserved
    }
}

namespace Stratum.Modules.Notification.Tests.Unit.Domain;

using FluentAssertions;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Domain.Services;
using Xunit;

public class SlaTrackerTests
{
    [Fact]
    public void CheckBreach_Should_Return_False_When_NoSla()
    {
        var record = DeliveryRecord.Create("tmpl", "email@test.com", null, null, null);

        SlaTracker.CheckBreach(record, null).Should().BeFalse();
    }

    [Fact]
    public void CheckBreach_Should_Return_False_When_Delivered()
    {
        var record = DeliveryRecord.Create("tmpl", "email@test.com", null, null, null);
        record.MarkDelivered();

        var sla = DeliverySla.Create(TemplateCategory.Transactional, 1, null, null, null);

        SlaTracker.CheckBreach(record, sla).Should().BeFalse();
    }

    [Fact]
    public void CheckBreach_Should_Return_False_When_Already_Breached()
    {
        var record = CreateOldRecord();
        record.MarkSlaBreached();

        var sla = DeliverySla.Create(TemplateCategory.Transactional, 1, null, null, null);

        SlaTracker.CheckBreach(record, sla).Should().BeFalse();
    }

    [Fact]
    public void CheckBreach_Should_Return_True_For_Overdue_Record()
    {
        var record = CreateOldRecord();
        var sla = DeliverySla.Create(TemplateCategory.Transactional, 1, null, null, null);

        SlaTracker.CheckBreach(record, sla).Should().BeTrue();
    }

    [Fact]
    public void FindBreachedRecords_Should_Return_Empty_When_NoSla()
    {
        var records = new[] { DeliveryRecord.Create("tmpl", "email@test.com", null, null, null) };

        SlaTracker.FindBreachedRecords(records, null).Should().BeEmpty();
    }

    [Fact]
    public void FindBreachedRecords_Should_Skip_Already_Breached()
    {
        var record = CreateOldRecord();
        record.MarkSlaBreached();

        var sla = DeliverySla.Create(TemplateCategory.Transactional, 1, null, null, null);

        SlaTracker.FindBreachedRecords([record], sla).Should().BeEmpty();
    }

    [Fact]
    public void FindBreachedRecords_Should_Skip_Delivered_Records()
    {
        var record = CreateOldRecord();
        record.MarkDelivered();

        var sla = DeliverySla.Create(TemplateCategory.Transactional, 1, null, null, null);

        SlaTracker.FindBreachedRecords([record], sla).Should().BeEmpty();
    }

    [Fact]
    public void FindBreachedRecords_Should_Find_Old_Undelivered_Records()
    {
        var record = CreateOldRecord();

        var sla = DeliverySla.Create(TemplateCategory.Transactional, 1, null, null, null);

        SlaTracker.FindBreachedRecords([record], sla).Should().HaveCount(1);
    }

    private static DeliveryRecord CreateOldRecord()
    {
        return DeliveryRecord.Reconstitute(
            Guid.NewGuid(),
            null,
            "tmpl",
            "email@test.com",
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            null,
            null,
            0,
            false,
            null);
    }
}

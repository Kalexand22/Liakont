namespace Stratum.Modules.Job.Tests.Unit.Entities;

using FluentAssertions;
using Stratum.Modules.Job.Domain.Entities;
using Xunit;

public class JobScheduleTests
{
    private static readonly Guid TestCompanyId = Guid.NewGuid();
    private static readonly DateTimeOffset FutureNextRun = DateTimeOffset.UtcNow.AddHours(1);

    [Fact]
    public void Create_Should_Succeed_With_Valid_Parameters()
    {
        var schedule = JobSchedule.Create(
            "daily-report",
            "0 9 * * *",
            "ReportGenerator",
            "{\"type\":\"daily\"}",
            TestCompanyId,
            FutureNextRun);

        schedule.Id.Should().NotBeEmpty();
        schedule.Name.Should().Be("daily-report");
        schedule.CronExpression.Should().Be("0 9 * * *");
        schedule.JobType.Should().Be("ReportGenerator");
        schedule.PayloadTemplate.Should().Be("{\"type\":\"daily\"}");
        schedule.IsActive.Should().BeTrue();
        schedule.NextRunAt.Should().Be(FutureNextRun);
        schedule.LastRunAt.Should().BeNull();
        schedule.CompanyId.Should().Be(TestCompanyId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Throw_When_Name_Empty(string? name)
    {
        var act = () => JobSchedule.Create(name!, "0 9 * * *", "Type", "{}", TestCompanyId, FutureNextRun);
        act.Should().Throw<ArgumentException>().WithMessage("*INV-JOB-005*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Throw_When_CronExpression_Empty(string? cron)
    {
        var act = () => JobSchedule.Create("test", cron!, "Type", "{}", TestCompanyId, FutureNextRun);
        act.Should().Throw<ArgumentException>().WithMessage("*INV-JOB-004*");
    }

    [Fact]
    public void Create_Should_Throw_When_JobType_Empty()
    {
        var act = () => JobSchedule.Create("test", "0 9 * * *", string.Empty, "{}", TestCompanyId, FutureNextRun);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_Should_Modify_Fields()
    {
        var schedule = JobSchedule.Create("old-name", "0 9 * * *", "OldType", "{}", TestCompanyId, FutureNextRun);
        var newNextRun = DateTimeOffset.UtcNow.AddMinutes(30);

        schedule.Update("new-name", "*/30 * * * *", "NewType", "{\"updated\":true}", newNextRun);

        schedule.Name.Should().Be("new-name");
        schedule.CronExpression.Should().Be("*/30 * * * *");
        schedule.JobType.Should().Be("NewType");
        schedule.PayloadTemplate.Should().Be("{\"updated\":true}");
        schedule.NextRunAt.Should().Be(newNextRun);
        schedule.UpdatedAt.Should().BeOnOrAfter(schedule.CreatedAt);
    }

    [Fact]
    public void Toggle_Should_Deactivate_Active_Schedule()
    {
        var schedule = JobSchedule.Create("test", "0 9 * * *", "Type", "{}", TestCompanyId, FutureNextRun);
        schedule.IsActive.Should().BeTrue();

        schedule.Toggle();

        schedule.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Toggle_Should_Activate_And_Set_NextRunAt_When_Provided()
    {
        var schedule = JobSchedule.Create("test", "0 9 * * *", "Type", "{}", TestCompanyId, FutureNextRun);
        schedule.Toggle(); // Deactivate
        schedule.IsActive.Should().BeFalse();

        var newNextRun = DateTimeOffset.UtcNow.AddHours(2);
        schedule.Toggle(newNextRun); // Reactivate

        schedule.IsActive.Should().BeTrue();
        schedule.NextRunAt.Should().Be(newNextRun);
    }

    [Fact]
    public void MarkExecuted_Should_Update_LastRunAt_And_NextRunAt()
    {
        var schedule = JobSchedule.Create("test", "0 9 * * *", "Type", "{}", TestCompanyId, FutureNextRun);
        var newNextRun = DateTimeOffset.UtcNow.AddHours(24);

        schedule.MarkExecuted(newNextRun);

        schedule.LastRunAt.Should().NotBeNull();
        schedule.LastRunAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        schedule.NextRunAt.Should().Be(newNextRun);
    }

    [Fact]
    public void Reconstitute_Should_Restore_All_Fields()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var nextRun = DateTimeOffset.UtcNow.AddHours(1);
        var lastRun = DateTimeOffset.UtcNow.AddHours(-1);
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var updated = DateTimeOffset.UtcNow;

        var schedule = JobSchedule.Reconstitute(
            id,
            "test-schedule",
            "0 9 * * *",
            "TestJob",
            "{\"key\":1}",
            false,
            nextRun,
            lastRun,
            companyId,
            created,
            updated);

        schedule.Id.Should().Be(id);
        schedule.Name.Should().Be("test-schedule");
        schedule.CronExpression.Should().Be("0 9 * * *");
        schedule.JobType.Should().Be("TestJob");
        schedule.PayloadTemplate.Should().Be("{\"key\":1}");
        schedule.IsActive.Should().BeFalse();
        schedule.NextRunAt.Should().Be(nextRun);
        schedule.LastRunAt.Should().Be(lastRun);
        schedule.CompanyId.Should().Be(companyId);
        schedule.CreatedAt.Should().Be(created);
        schedule.UpdatedAt.Should().Be(updated);
    }
}

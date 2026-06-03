namespace Stratum.Modules.Job.Tests.Unit.Entities;

using FluentAssertions;
using Stratum.Modules.Job.Domain.Entities;
using Xunit;

public class JobEntryTests
{
    [Fact]
    public void Create_Should_Succeed_With_Valid_Parameters()
    {
        var job = JobEntry.Create("MyJob", "{\"key\":\"value\"}");

        job.Id.Should().NotBeEmpty();
        job.Type.Should().Be("MyJob");
        job.Payload.Should().Be("{\"key\":\"value\"}");
        job.Status.Value.Should().Be("Pending");
        job.Priority.Should().Be(0);
        job.MaxRetries.Should().Be(3);
        job.RetryCount.Should().Be(0);
        job.ScheduledAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        job.StartedAt.Should().BeNull();
        job.CompletedAt.Should().BeNull();
        job.ErrorMessage.Should().BeNull();
        job.CompanyId.Should().BeNull();
    }

    [Fact]
    public void Create_Should_Accept_Optional_Parameters()
    {
        var scheduled = DateTimeOffset.UtcNow.AddHours(1);
        var companyId = Guid.NewGuid();

        var job = JobEntry.Create("MyJob", "{}", priority: 5, maxRetries: 10, scheduledAt: scheduled, companyId: companyId);

        job.Priority.Should().Be(5);
        job.MaxRetries.Should().Be(10);
        job.ScheduledAt.Should().Be(scheduled);
        job.CompanyId.Should().Be(companyId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Throw_When_Type_Empty(string? type)
    {
        var act = () => JobEntry.Create(type!, "{}");
        act.Should().Throw<ArgumentException>().WithMessage("*INV-JOB-001*");
    }

    [Fact]
    public void Create_Should_Throw_When_Payload_Null()
    {
        var act = () => JobEntry.Create("MyJob", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MarkRunning_Should_Succeed_When_Pending()
    {
        var job = JobEntry.Create("MyJob", "{}");

        job.MarkRunning();

        job.Status.Value.Should().Be("Running");
        job.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkRunning_Should_Throw_When_Not_Pending()
    {
        var job = JobEntry.Create("MyJob", "{}");
        job.MarkRunning();

        var act = () => job.MarkRunning();
        act.Should().Throw<InvalidOperationException>().WithMessage("*INV-JOB-002*");
    }

    [Fact]
    public void MarkCompleted_Should_Succeed_When_Running()
    {
        var job = JobEntry.Create("MyJob", "{}");
        job.MarkRunning();

        job.MarkCompleted();

        job.Status.Value.Should().Be("Completed");
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkCompleted_Should_Throw_When_Not_Running()
    {
        var job = JobEntry.Create("MyJob", "{}");

        var act = () => job.MarkCompleted();
        act.Should().Throw<InvalidOperationException>().WithMessage("*INV-JOB-002*");
    }

    [Fact]
    public void MarkFailed_Should_ReturnToPending_When_RetriesRemain()
    {
        var job = JobEntry.Create("MyJob", "{}", maxRetries: 3);
        job.MarkRunning();

        job.MarkFailed("Something went wrong");

        job.Status.Value.Should().Be("Pending");
        job.RetryCount.Should().Be(1);
        job.ErrorMessage.Should().Be("Something went wrong");
    }

    [Fact]
    public void MarkFailed_Should_Throw_When_Not_Running()
    {
        var job = JobEntry.Create("MyJob", "{}");

        var act = () => job.MarkFailed("error");
        act.Should().Throw<InvalidOperationException>().WithMessage("*INV-JOB-002*");
    }

    [Fact]
    public void MarkFailed_Should_MarkDead_When_MaxRetriesReached()
    {
        var job = JobEntry.Create("MyJob", "{}", maxRetries: 2);

        // First failure
        job.MarkRunning();
        job.MarkFailed("error 1");
        job.Status.Value.Should().Be("Pending");
        job.RetryCount.Should().Be(1);

        // Second failure -> Dead
        job.MarkRunning();
        job.MarkFailed("error 2");
        job.Status.Value.Should().Be("Dead");
        job.RetryCount.Should().Be(2);
    }

    [Fact]
    public void Dead_Job_Cannot_Be_Started()
    {
        var job = JobEntry.Create("MyJob", "{}", maxRetries: 1);
        job.MarkRunning();
        job.MarkFailed("error");

        job.Status.Value.Should().Be("Dead");

        var act = () => job.MarkRunning();
        act.Should().Throw<InvalidOperationException>().WithMessage("*INV-JOB-002*");
    }

    [Fact]
    public void Completed_Job_Cannot_Be_Started()
    {
        var job = JobEntry.Create("MyJob", "{}");
        job.MarkRunning();
        job.MarkCompleted();

        var act = () => job.MarkRunning();
        act.Should().Throw<InvalidOperationException>().WithMessage("*INV-JOB-002*");
    }

    [Fact]
    public void Reconstitute_Should_Restore_All_Fields()
    {
        var id = Guid.NewGuid();
        var scheduledAt = DateTimeOffset.UtcNow;
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        var companyId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        var job = JobEntry.Reconstitute(
            id,
            "MyJob",
            "{\"a\":1}",
            "Running",
            5,
            3,
            1,
            scheduledAt,
            startedAt,
            null,
            "prev error",
            companyId,
            createdAt);

        job.Id.Should().Be(id);
        job.Type.Should().Be("MyJob");
        job.Payload.Should().Be("{\"a\":1}");
        job.Status.Value.Should().Be("Running");
        job.Priority.Should().Be(5);
        job.MaxRetries.Should().Be(3);
        job.RetryCount.Should().Be(1);
        job.ScheduledAt.Should().Be(scheduledAt);
        job.StartedAt.Should().Be(startedAt);
        job.CompletedAt.Should().BeNull();
        job.ErrorMessage.Should().Be("prev error");
        job.CompanyId.Should().Be(companyId);
        job.CreatedAt.Should().Be(createdAt);
    }
}

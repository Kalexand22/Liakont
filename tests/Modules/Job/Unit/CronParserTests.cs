namespace Stratum.Modules.Job.Tests.Unit;

using FluentAssertions;
using Stratum.Modules.Job.Infrastructure;
using Xunit;

public class CronParserTests
{
    [Theory]
    [InlineData("0 9 * * *")]
    [InlineData("*/5 * * * *")]
    [InlineData("0 0 1 * *")]
    [InlineData("30 14 * * 1-5")]
    public void Validate_Should_Accept_Valid_CronExpressions(string cron)
    {
        var act = () => CronParser.Validate(cron);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Should_Throw_When_Empty(string? cron)
    {
        var act = () => CronParser.Validate(cron!);
        act.Should().Throw<ArgumentException>().WithMessage("*INV-JOB-004*");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("* * *")]
    [InlineData("60 * * * *")]
    public void Validate_Should_Throw_When_Invalid(string cron)
    {
        var act = () => CronParser.Validate(cron);
        act.Should().Throw<ArgumentException>().WithMessage("*INV-JOB-004*");
    }

    [Fact]
    public void CalculateNextRun_Should_Return_Future_Time()
    {
        var from = new DateTimeOffset(2026, 3, 23, 12, 0, 0, TimeSpan.Zero);
        var next = CronParser.CalculateNextRun("0 9 * * *", from);

        next.Should().BeAfter(from);
        next.Hour.Should().Be(9);
        next.Day.Should().Be(24);
    }

    [Fact]
    public void CalculateNextRun_EveryFiveMinutes_Should_Return_Near_Future()
    {
        var from = new DateTimeOffset(2026, 3, 23, 12, 0, 0, TimeSpan.Zero);
        var next = CronParser.CalculateNextRun("*/5 * * * *", from);

        next.Should().Be(new DateTimeOffset(2026, 3, 23, 12, 5, 0, TimeSpan.Zero));
    }
}

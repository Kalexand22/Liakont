namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class RelativeDatePeriodResolverTests
{
    private static readonly DateTimeOffset Reference = new(2026, 4, 14, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public void TodayShouldSpanEntireDay()
    {
        var (start, end) = RelativeDatePeriodResolver.Resolve(RelativeDatePeriod.Today, Reference);

        start.Should().Be(new DateTimeOffset(2026, 4, 14, 0, 0, 0, TimeSpan.Zero));
        end.Date.Should().Be(new DateTime(2026, 4, 14));
        end.Hour.Should().Be(23);
        end.Minute.Should().Be(59);
    }

    [Fact]
    public void YesterdayShouldSpanPreviousDay()
    {
        var (start, end) = RelativeDatePeriodResolver.Resolve(RelativeDatePeriod.Yesterday, Reference);

        start.Should().Be(new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.Zero));
        end.Date.Should().Be(new DateTime(2026, 4, 13));
    }

    [Fact]
    public void Last7DaysShouldSpanSevenDaysBack()
    {
        var (start, end) = RelativeDatePeriodResolver.Resolve(RelativeDatePeriod.Last7Days, Reference);

        start.Should().Be(new DateTimeOffset(2026, 4, 8, 0, 0, 0, TimeSpan.Zero));
        end.Date.Should().Be(new DateTime(2026, 4, 14));
    }

    [Fact]
    public void Last30DaysShouldSpanThirtyDaysBack()
    {
        var (start, end) = RelativeDatePeriodResolver.Resolve(RelativeDatePeriod.Last30Days, Reference);

        start.Should().Be(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero));
        end.Date.Should().Be(new DateTime(2026, 4, 14));
    }

    [Fact]
    public void ThisMonthShouldSpanEntireMonth()
    {
        var (start, end) = RelativeDatePeriodResolver.Resolve(RelativeDatePeriod.ThisMonth, Reference);

        start.Should().Be(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        end.Date.Should().Be(new DateTime(2026, 4, 30));
    }

    [Fact]
    public void LastMonthShouldSpanPreviousMonth()
    {
        var (start, end) = RelativeDatePeriodResolver.Resolve(RelativeDatePeriod.LastMonth, Reference);

        start.Should().Be(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        end.Date.Should().Be(new DateTime(2026, 3, 31));
    }

    [Fact]
    public void ThisQuarterShouldSpanCurrentQuarter()
    {
        var (start, end) = RelativeDatePeriodResolver.Resolve(RelativeDatePeriod.ThisQuarter, Reference);

        start.Should().Be(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        end.Date.Should().Be(new DateTime(2026, 6, 30));
    }

    [Fact]
    public void ThisYearShouldSpanEntireYear()
    {
        var (start, end) = RelativeDatePeriodResolver.Resolve(RelativeDatePeriod.ThisYear, Reference);

        start.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        end.Date.Should().Be(new DateTime(2026, 12, 31));
    }
}

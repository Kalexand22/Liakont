namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Resolves a <see cref="RelativeDatePeriod"/> into concrete UTC date bounds.
/// The resolution uses UTC as the reference. For user-timezone-aware resolution,
/// callers should convert the <paramref name="referenceUtc"/> to the user's
/// local time before calling, or use the overload accepting a <see cref="TimeZoneInfo"/>.
/// </summary>
public static class RelativeDatePeriodResolver
{
    /// <summary>
    /// Resolves the period into inclusive [start, end] bounds in UTC.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Resolve(
        RelativeDatePeriod period,
        DateTimeOffset referenceUtc)
    {
        return Resolve(period, referenceUtc, TimeZoneInfo.Utc);
    }

    /// <summary>
    /// Resolves the period into inclusive [start, end] bounds.
    /// Computation is done in the user's timezone, then converted back to UTC.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Resolve(
        RelativeDatePeriod period,
        DateTimeOffset referenceUtc,
        TimeZoneInfo userTimeZone)
    {
        var localNow = TimeZoneInfo.ConvertTime(referenceUtc, userTimeZone);
        var localDate = localNow.Date;

        var (startLocal, endLocal) = period switch
        {
            RelativeDatePeriod.Today => (
                localDate,
                localDate.AddDays(1).AddTicks(-1)),

            RelativeDatePeriod.Yesterday => (
                localDate.AddDays(-1),
                localDate.AddTicks(-1)),

            RelativeDatePeriod.Last7Days => (
                localDate.AddDays(-6),
                localDate.AddDays(1).AddTicks(-1)),

            RelativeDatePeriod.Last30Days => (
                localDate.AddDays(-29),
                localDate.AddDays(1).AddTicks(-1)),

            RelativeDatePeriod.ThisMonth => (
                new DateTime(localDate.Year, localDate.Month, 1),
                new DateTime(localDate.Year, localDate.Month, 1)
                    .AddMonths(1).AddTicks(-1)),

            RelativeDatePeriod.LastMonth => (
                new DateTime(localDate.Year, localDate.Month, 1).AddMonths(-1),
                new DateTime(localDate.Year, localDate.Month, 1).AddTicks(-1)),

            RelativeDatePeriod.ThisQuarter => (
                GetQuarterStart(localDate),
                GetQuarterStart(localDate).AddMonths(3).AddTicks(-1)),

            RelativeDatePeriod.ThisYear => (
                new DateTime(localDate.Year, 1, 1),
                new DateTime(localDate.Year + 1, 1, 1).AddTicks(-1)),

            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown relative date period."),
        };

        var startUtc = new DateTimeOffset(startLocal, userTimeZone.GetUtcOffset(startLocal));
        var endUtc = new DateTimeOffset(endLocal, userTimeZone.GetUtcOffset(endLocal));

        return (startUtc.ToUniversalTime(), endUtc.ToUniversalTime());
    }

    private static DateTime GetQuarterStart(DateTime date)
    {
        var quarterMonth = (((date.Month - 1) / 3) * 3) + 1;
        return new DateTime(date.Year, quarterMonth, 1);
    }
}

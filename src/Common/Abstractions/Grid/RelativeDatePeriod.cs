namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Predefined relative date periods for <see cref="FilterOperator.RelativePeriod"/>.
/// Resolved at runtime in the user's timezone.
/// </summary>
public enum RelativeDatePeriod
{
    /// <summary>Current day (00:00 to 23:59:59.999).</summary>
    Today,

    /// <summary>Previous day.</summary>
    Yesterday,

    /// <summary>Current day minus 7 days to today.</summary>
    Last7Days,

    /// <summary>Current day minus 30 days to today.</summary>
    Last30Days,

    /// <summary>First to last day of the current month.</summary>
    ThisMonth,

    /// <summary>First to last day of the previous month.</summary>
    LastMonth,

    /// <summary>First to last day of the current quarter.</summary>
    ThisQuarter,

    /// <summary>First to last day of the current year.</summary>
    ThisYear,
}

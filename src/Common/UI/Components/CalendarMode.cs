namespace Stratum.Common.UI.Components;

/// <summary>Display mode for <see cref="Calendar{TItem}"/>.</summary>
public enum CalendarMode
{
    /// <summary>Monthly grid — 7 columns × N rows.</summary>
    Month,

    /// <summary>Weekly time-grid — 7 columns × 24 hour rows.</summary>
    Week,

    /// <summary>Daily time-grid — 1 column × 24 hour rows.</summary>
    Day,
}

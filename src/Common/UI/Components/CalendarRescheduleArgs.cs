namespace Stratum.Common.UI.Components;

/// <summary>
/// Arguments for a calendar reschedule event (drag-drop).
/// </summary>
public sealed record CalendarRescheduleArgs<TItem>(
    TItem Item,
    DateOnly OldDate,
    DateOnly NewDate);

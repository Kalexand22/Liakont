namespace Stratum.Common.UI.Models;

/// <summary>
/// Data raised when a Gantt bar is drag-resized to new dates.
/// </summary>
/// <typeparam name="TItem">The data item type.</typeparam>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix — EventArgs is appropriate for event data
public sealed record GanttBarResizeEventArgs<TItem>(
    TItem Item,
    DateOnly NewStartDate,
    DateOnly NewEndDate);
#pragma warning restore CA1711

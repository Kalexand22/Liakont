namespace Stratum.Common.UI.Components;

/// <summary>
/// Arguments supplied to <see cref="Calendar{TItem}.OnEventMove"/> when the user
/// drag-drops a calendar event to a new time slot.
/// </summary>
/// <typeparam name="TItem">The event item type.</typeparam>
public record EventMoveArgs<TItem>(
    TItem Item,
    DateTime OldStart,
    DateTime? OldEnd,
    DateTime NewStart,
    DateTime? NewEnd);

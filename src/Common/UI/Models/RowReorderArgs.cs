namespace Stratum.Common.UI.Models;

/// <summary>
/// Arguments passed to <c>OnRowReorder</c> when the user drag-and-drops a row to a new position.
/// </summary>
/// <param name="FromIndex">Original index of the dragged row.</param>
/// <param name="ToIndex">New index where the row was dropped.</param>
public sealed record RowReorderArgs(int FromIndex, int ToIndex);

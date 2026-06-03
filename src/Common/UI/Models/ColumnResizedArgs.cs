namespace Stratum.Common.UI.Models;

/// <summary>
/// Raised when a column is resized by dragging its border.
/// </summary>
/// <param name="Property">The column's data property name.</param>
/// <param name="Title">The column header text.</param>
/// <param name="NewWidth">The new width (CSS value, e.g. "180px").</param>
public sealed record ColumnResizedArgs(string? Property, string Title, string NewWidth);

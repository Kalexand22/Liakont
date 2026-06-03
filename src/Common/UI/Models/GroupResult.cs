namespace Stratum.Common.UI.Models;

/// <summary>
/// Represents a group header in StratumDataGrid grouping.
/// Used as the context for <see cref="Components.StratumDataGrid{TItem}.GroupTemplate"/>.
/// </summary>
/// <param name="Value">The grouped field value (e.g. "Electronics", "Pending").</param>
/// <param name="Count">Number of items in this group.</param>
public sealed record GroupResult(object? Value, int Count);

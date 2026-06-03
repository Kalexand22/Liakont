namespace Stratum.Common.UI.Models;

using Stratum.Common.Abstractions.Grid;

/// <summary>
/// Arguments passed when StratumDataGrid requests data from the server.
/// This is a Stratum type — it does not reference any Radzen types.
/// </summary>
/// <param name="Skip">Number of items to skip (for paging).</param>
/// <param name="Top">Number of items to take (page size).</param>
/// <param name="SortField">Field name to sort by, or null if unsorted.</param>
/// <param name="Direction">Sort direction when <paramref name="SortField"/> is set.</param>
/// <param name="RequestedFields">
/// Property paths of visible columns, including related-table paths (e.g. "Customer.Name").
/// Allows the query handler to optimize joins/includes. Empty when no column registry is active.
/// </param>
/// <param name="Filter">
/// Active advanced filter expression, or null when no advanced filter is applied.
/// Backend query handlers use <see cref="IFilterExpressionBuilder{TItem}"/> to convert
/// this to a LINQ Where clause.
/// </param>
public sealed record LoadDataArgs(
    int Skip,
    int Top,
    string? SortField,
    SortDirection? Direction,
    IReadOnlyList<string>? RequestedFields = null,
    FilterGroup? Filter = null)
{
    /// <summary>
    /// Property paths of visible columns. Never null at runtime (defaults to empty).
    /// </summary>
    public IReadOnlyList<string> RequestedFields { get; init; } = RequestedFields ?? Array.Empty<string>();
}

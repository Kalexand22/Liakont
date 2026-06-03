namespace Stratum.Common.UI.Models;

/// <summary>
/// Cascading context from <see cref="Components.StratumDataGrid{TItem}"/> to
/// <see cref="Components.StratumColumn{TItem}"/>. Provides access to the grid's
/// current data so that columns can compute aggregate footer values.
/// </summary>
/// <typeparam name="TItem">The grid item type.</typeparam>
public sealed class DataGridAggregateContext<TItem>
{
    /// <summary>Current data displayed in the grid (may be a single page).</summary>
    public IReadOnlyList<TItem>? Data { get; set; }

    /// <summary>
    /// Set to true by any <see cref="Components.StratumColumn{TItem}"/> that has
    /// <see cref="AggregateFunc"/> != None. Used by the grid to enable the footer row.
    /// </summary>
    public bool HasAggregates { get; set; }
}

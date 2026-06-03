namespace Stratum.Common.UI.Models;

/// <summary>
/// Cascading context from <see cref="Components.StratumDataGrid{TItem}"/> to
/// <see cref="Components.StratumColumn{TItem}"/>. Tracks which item is currently
/// being edited so that columns can render their <c>EditTemplate</c> instead of
/// the normal display template.
/// </summary>
/// <remarks>
/// Radzen's built-in <c>EditRow</c> API is silently ignored when called from within
/// Radzen's own event handlers (CellClick, RowClick, etc.). This context bypasses
/// Radzen's edit mechanism entirely and lets StratumColumn manage edit/display
/// rendering via the standard Radzen Template child content.
/// </remarks>
/// <typeparam name="TItem">The grid item type.</typeparam>
internal sealed class DataGridEditingContext<TItem>
    where TItem : notnull
{
    /// <summary>The item currently in edit mode, or default if no row is being edited.</summary>
    public TItem EditingItem { get; set; } = default!;

    /// <summary>Whether any item is currently being edited.</summary>
    public bool IsEditing { get; set; }
}

namespace Stratum.Common.UI.Services;

using Stratum.Common.UI.Models;

/// <summary>
/// Registry of available columns for a grid bound to <typeparamref name="TItem"/>.
/// Each grid context (e.g. PartyList, InvoiceList) implements this interface to declare
/// its available columns, including related-table columns resolved via property paths.
/// </summary>
/// <typeparam name="TItem">The DTO type displayed in the grid.</typeparam>
public interface IColumnRegistry<TItem>
{
    /// <summary>
    /// Returns all available columns for this grid context, including both
    /// base-table and related-table columns.
    /// </summary>
    IReadOnlyList<ColumnDefinition> GetAvailableColumns();

    /// <summary>
    /// Returns columns grouped by <see cref="ColumnDefinition.Category"/>.
    /// Keys are category names (e.g. "Main", "Customer"), values are columns
    /// sorted by <see cref="ColumnDefinition.SortOrder"/>.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<ColumnDefinition>> GetColumnsByCategory();

    /// <summary>
    /// Returns columns that are visible by default (when no user preference is saved).
    /// </summary>
    IReadOnlyList<ColumnDefinition> GetDefaultVisibleColumns();

    /// <summary>
    /// Looks up a column by its key. Returns <c>null</c> if not found.
    /// </summary>
    ColumnDefinition? GetColumn(string key);

    /// <summary>
    /// Returns the property paths that full-text search should target based on
    /// the given visible column keys. Resolves display columns to their declared
    /// searchable sub-fields, includes text/enum columns directly, and skips
    /// non-text columns.
    /// </summary>
    /// <param name="visibleKeys">Visible column keys, or null for defaults.</param>
    IReadOnlyList<string> GetSearchableFields(IReadOnlyList<string>? visibleKeys);
}

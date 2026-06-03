namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Persists per-user column preferences for grids.
/// Returns <c>null</c> from <see cref="GetPreferenceAsync"/> when no preference is stored;
/// consumers should fall back to the column registry's default visible columns.
/// </summary>
public interface IGridPreferenceService
{
    /// <summary>
    /// Returns the stored preference for the given user and grid, or <c>null</c> when none exists.
    /// </summary>
    Task<UserGridPreference?> GetPreferenceAsync(Guid userId, string gridKey, CancellationToken ct = default);

    /// <summary>
    /// Creates or replaces the column preference for the given user and grid.
    /// </summary>
    Task SavePreferenceAsync(Guid userId, string gridKey, IReadOnlyList<string> columnKeys, CancellationToken ct = default);

    /// <summary>
    /// Persists the preferred view mode for the given user and grid.
    /// Creates the preference row if it does not exist yet.
    /// </summary>
    Task SaveViewPreferenceAsync(Guid userId, string gridKey, ViewKind viewKind, CancellationToken ct = default);

    /// <summary>
    /// Persists the serialized <see cref="GridFilterState"/> so the page can restore the
    /// last filter state when the user navigates back (GFI14).
    /// Pass <c>null</c> to clear any previously stored state.
    /// Creates the preference row if it does not exist yet.
    /// </summary>
    Task SaveFilterStateAsync(Guid userId, string gridKey, string? filterStateJson, CancellationToken ct = default);

    /// <summary>
    /// Persists the per-column widths (GUX09) for the given user and grid. Widths are
    /// stored as CSS tokens (e.g. "240px") keyed by column key. Passing an empty
    /// dictionary clears previously stored widths. Creates the preference row if it
    /// does not exist yet.
    /// </summary>
    Task SaveColumnWidthsAsync(
        Guid userId,
        string gridKey,
        IReadOnlyDictionary<string, string> columnWidths,
        CancellationToken ct = default);
}

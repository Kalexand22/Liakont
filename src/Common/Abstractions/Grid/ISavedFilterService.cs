namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Persists per-user saved filters for grids.
/// One default filter per user per grid is enforced: calling
/// <see cref="SetDefaultAsync"/> clears the previous default.
/// </summary>
public interface ISavedFilterService
{
    /// <summary>
    /// Returns all saved filters for the given user and grid,
    /// including filters shared by other users.
    /// </summary>
    Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default);

    /// <summary>
    /// Returns a single saved filter by id, or <c>null</c> if not found.
    /// </summary>
    Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a saved filter. Returns the persisted filter with generated id/timestamps.
    /// </summary>
    Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Deletes a saved filter by id.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Sets the given filter as default for its user and grid.
    /// Clears any previous default for the same user/grid combination.
    /// </summary>
    Task SetDefaultAsync(Guid id, CancellationToken ct = default);
}

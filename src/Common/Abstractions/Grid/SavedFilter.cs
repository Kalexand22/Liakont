namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Persisted per-user saved filter for a specific grid.
/// </summary>
/// <param name="Id">Row identifier.</param>
/// <param name="UserId">Owning user.</param>
/// <param name="GridKey">
/// Identifies the grid context using the convention module.page.grid
/// (e.g. "Sales.InvoiceList.Main").
/// </param>
/// <param name="Name">User-defined name for the filter.</param>
/// <param name="FilterGroup">The filter criteria (serialized as JSON in storage).</param>
/// <param name="IsDefault">Whether this filter auto-applies on grid load for the user.</param>
/// <param name="SharedWith">Sharing scope: None or Everyone.</param>
/// <param name="CreatedAt">Row creation timestamp.</param>
/// <param name="UpdatedAt">Last modification timestamp.</param>
/// <param name="Source">
/// Which builder produced this filter. Used at reload time to restore the filter
/// into its original source (simple chip list vs advanced builder) per DF-02
/// exception persistance. Defaults to <see cref="SavedFilterSource.Advanced"/>
/// for backward compatibility with pre-GFI10 rows.
/// </param>
public sealed record SavedFilter(
    Guid Id,
    Guid UserId,
    string GridKey,
    string Name,
    FilterGroup FilterGroup,
    bool IsDefault,
    SharedScope SharedWith,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    SavedFilterSource Source = SavedFilterSource.Advanced);

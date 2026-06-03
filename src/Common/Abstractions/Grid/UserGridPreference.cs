namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Persisted per-user column preference for a specific grid.
/// </summary>
/// <param name="Id">Row identifier.</param>
/// <param name="UserId">Owning user.</param>
/// <param name="GridKey">
/// Identifies the grid context using the convention module.page.grid
/// (e.g. "Sales.InvoiceList.Main").
/// </param>
/// <param name="ColumnKeys">
/// Ordered list of visible column keys. An empty list means the user never
/// persisted an explicit column-visibility preference — for example because
/// they only saved a widths-only (GUX09) or view-kind-only update — and
/// consumers must fall back to the registry's default visible columns.
/// </param>
/// <param name="CreatedAt">Row creation timestamp.</param>
/// <param name="UpdatedAt">Last modification timestamp.</param>
/// <param name="PreferredViewKind">Optional preferred view mode for this grid.</param>
/// <param name="FilterStateJson">
/// Serialized <see cref="GridFilterState"/> for automatic restoration on return to
/// the page (GFI14). Null when no filter state has ever been persisted for this user/grid.
/// </param>
/// <param name="ColumnWidths">
/// Persisted column widths keyed by column key. Each value is a CSS width token
/// (e.g. "240px") ready to flow back into Radzen's width binding (GUX09). Null when
/// no width has ever been persisted for this user/grid; an empty dictionary means
/// widths were cleared explicitly.
/// </param>
public sealed record UserGridPreference(
    Guid Id,
    Guid UserId,
    string GridKey,
    IReadOnlyList<string> ColumnKeys,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    ViewKind? PreferredViewKind = null,
    string? FilterStateJson = null,
    IReadOnlyDictionary<string, string>? ColumnWidths = null);

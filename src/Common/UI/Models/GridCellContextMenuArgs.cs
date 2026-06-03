namespace Stratum.Common.UI.Models;

/// <summary>
/// Payload raised by <c>StratumDataGrid.OnCellContextMenu</c> when a user
/// right-clicks a data cell. Carries the target row, the column metadata and
/// resolved value so that consumers can build simple filters (Equals / NotEquals)
/// via <c>GridFilterState</c>.
/// </summary>
public sealed record GridCellContextMenuArgs<TItem>(
    TItem Item,
    string ColumnKey,
    string Field,
    object? Value,
    string DisplayValue,
    double ClientX,
    double ClientY,
    EntityReferenceTarget? EntityReferenceTarget = null);

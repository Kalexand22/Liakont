namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Enumerates the available view modes for list pages.
/// Extensible: new view kinds can be added without breaking existing code.
/// </summary>
public enum ViewKind
{
    /// <summary>Traditional tabular data grid (default).</summary>
    Table,

    /// <summary>Responsive card grid layout.</summary>
    Card,

    /// <summary>Column-based board grouped by a category property.</summary>
    Kanban,

    /// <summary>Date-based calendar layout.</summary>
    Calendar,
}

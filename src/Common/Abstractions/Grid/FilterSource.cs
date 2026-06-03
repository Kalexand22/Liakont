namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Identifies the origin of a filter, determining which editor opens on chip click.
/// </summary>
public enum FilterSource
{
    /// <summary>Created via the simple filter builder popover.</summary>
    Simple,

    /// <summary>Created via the advanced StratumFilterBuilder popup.</summary>
    Advanced,

    /// <summary>Represents the global search text.</summary>
    GlobalSearch,
}

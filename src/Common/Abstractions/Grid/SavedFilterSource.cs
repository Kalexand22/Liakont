namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Identifies which builder produced a <see cref="SavedFilter"/> so it can be restored
/// to its original source on reload (DF-02 exception persistance).
/// </summary>
public enum SavedFilterSource : short
{
    /// <summary>Filter created via the simple (single-criterion) builder. Persisted as a mono-criterion FilterGroup.</summary>
    Simple = 0,

    /// <summary>Filter created via the advanced StratumFilterBuilder popup.</summary>
    Advanced = 1,
}

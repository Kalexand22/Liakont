namespace Stratum.Common.UI.Models;

/// <summary>
/// Aggregate function applied to a column in StratumDataGrid.
/// Used in the column footer row to display computed values.
/// </summary>
public enum AggregateFunc
{
    /// <summary>No aggregation (default).</summary>
    None = 0,

    /// <summary>Sum of all numeric values in the column.</summary>
    Sum,

    /// <summary>Average of all numeric values in the column.</summary>
    Average,

    /// <summary>Count of non-null values in the column.</summary>
    Count,

    /// <summary>Minimum value in the column.</summary>
    Min,

    /// <summary>Maximum value in the column.</summary>
    Max,
}

namespace Stratum.Common.UI.Models;

/// <summary>Event args raised when the user clicks a data point on a chart.</summary>
public sealed record ChartPointClickArgs
{
    /// <summary>Name of the series that was clicked.</summary>
    public required string SeriesName { get; init; }

    /// <summary>Category label (X-axis value) of the clicked point.</summary>
    public required string Category { get; init; }

    /// <summary>Numeric value of the clicked point.</summary>
    public required double Value { get; init; }

    /// <summary>Zero-based index of the data point within its series.</summary>
    public required int DataIndex { get; init; }
}

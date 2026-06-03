namespace Stratum.Common.UI.Models;

/// <summary>Serializable chart configuration passed to the JS renderer.</summary>
public sealed record ChartConfig
{
    /// <summary>The chart visualization type.</summary>
    public ChartType Type { get; init; }

    /// <summary>Category labels (X-axis for cartesian charts, names for pie).</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Pre-resolved series data ready for the JS renderer.</summary>
    public IReadOnlyList<ChartSeriesData> Series { get; init; } = [];

    /// <summary>Whether series should be stacked.</summary>
    public bool Stacked { get; init; }

    /// <summary>Optional chart title.</summary>
    public string? Title { get; init; }

    /// <summary>Whether to display the legend. Default: true.</summary>
    public bool ShowLegend { get; init; } = true;

    /// <summary>Accessible label for the chart.</summary>
    public string? AriaLabel { get; init; }
}

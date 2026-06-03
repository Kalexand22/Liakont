namespace Stratum.Common.UI.Models;

/// <summary>Pre-resolved series data (name + values) ready for the JS renderer.</summary>
public sealed record ChartSeriesData
{
    /// <summary>Display name for the series.</summary>
    public required string Name { get; init; }

    /// <summary>Chart type for this series (may differ from the chart-level type).</summary>
    public ChartType Type { get; init; }

    /// <summary>Optional explicit color.</summary>
    public string? Color { get; init; }

    /// <summary>Numeric values for this series.</summary>
    public IReadOnlyList<double> Values { get; init; } = [];
}

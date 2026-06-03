namespace Stratum.Common.UI.Models;

/// <summary>
/// Defines a data series for the Chart component.
/// Each series maps a value field from the data source to a visual representation.
/// </summary>
public sealed record ChartSeries
{
    /// <summary>Display name shown in the legend.</summary>
    public required string Name { get; init; }

    /// <summary>Property name on TData that provides the numeric value.</summary>
    public required string ValueField { get; init; }

    /// <summary>Optional override of the chart-level <see cref="ChartType"/>.</summary>
    public ChartType? Type { get; init; }

    /// <summary>Optional explicit color (CSS color string). When null, the renderer picks from its palette.</summary>
    public string? Color { get; init; }
}

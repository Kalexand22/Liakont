namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Result of a WFS DescribeFeatureType operation.
/// </summary>
public sealed record FeatureTypeDescription
{
    public required string TypeName { get; init; }

    public required IReadOnlyList<FeatureProperty> Properties { get; init; }
}

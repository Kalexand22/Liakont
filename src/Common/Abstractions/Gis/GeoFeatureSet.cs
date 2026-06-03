namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// A GeoJSON FeatureCollection returned by WFS GetFeature.
/// </summary>
public sealed record GeoFeatureSet
{
    public required IReadOnlyList<GeoFeature> Features { get; init; }
}

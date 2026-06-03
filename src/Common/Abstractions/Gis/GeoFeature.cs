namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// A single GeoJSON Feature within a GeoFeatureCollection.
/// </summary>
public sealed record GeoFeature
{
    public string? Id { get; init; }

    public GeoJsonGeometry? Geometry { get; init; }

    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();
}

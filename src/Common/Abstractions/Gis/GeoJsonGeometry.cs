namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Immutable value object representing a GeoJSON geometry (RFC 7946).
/// Wraps the raw GeoJSON string; spatial operations are performed by <see cref="IGeoJsonService"/>.
/// </summary>
public sealed record GeoJsonGeometry
{
    public GeoJsonGeometry(string geoJson, string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(geoJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        GeoJson = geoJson;
        Type = type;
    }

    /// <summary>
    /// Raw GeoJSON geometry string (e.g. {"type":"Polygon","coordinates":[...]}).
    /// </summary>
    public string GeoJson { get; }

    /// <summary>
    /// The geometry type: Point, LineString, Polygon, MultiPoint, MultiLineString, MultiPolygon, GeometryCollection.
    /// </summary>
    public string Type { get; }
}

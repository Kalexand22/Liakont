namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Service for GeoJSON parsing, validation, and spatial computations.
/// Implementation uses NetTopologySuite internally (ADR-0016).
/// </summary>
public interface IGeoJsonService
{
    /// <summary>
    /// Parses a raw GeoJSON string into a <see cref="GeoJsonGeometry"/> value object.
    /// </summary>
    /// <exception cref="FormatException">If the GeoJSON is malformed.</exception>
    GeoJsonGeometry Parse(string geoJson);

    /// <summary>
    /// Validates that the geometry is well-formed and topologically valid.
    /// </summary>
    bool Validate(GeoJsonGeometry geometry);

    /// <summary>
    /// Computes the axis-aligned bounding box of the geometry.
    /// </summary>
    BoundingBox CalculateBoundingBox(GeoJsonGeometry geometry);

    /// <summary>
    /// Returns true if the two geometries share any interior point.
    /// </summary>
    bool Intersects(GeoJsonGeometry geometry1, GeoJsonGeometry geometry2);

    /// <summary>
    /// Returns true if <paramref name="geometry"/> fully contains <paramref name="point"/>.
    /// </summary>
    bool Contains(GeoJsonGeometry geometry, GeoJsonGeometry point);

    /// <summary>
    /// Computes the area of a polygon geometry in square meters.
    /// Returns 0 for non-polygon geometries.
    /// </summary>
    double Area(GeoJsonGeometry geometry);

    /// <summary>
    /// Serializes a <see cref="GeoJsonGeometry"/> back to a raw GeoJSON string.
    /// </summary>
    string Serialize(GeoJsonGeometry geometry);
}

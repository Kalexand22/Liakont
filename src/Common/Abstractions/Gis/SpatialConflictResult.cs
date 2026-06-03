namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Result of a spatial conflict check between two geometries.
/// </summary>
/// <param name="ExistingGeometryIndex">Index of the conflicting geometry in the existing geometries collection.</param>
/// <param name="OverlapAreaSquareMeters">The area of overlap in square meters (0 for non-polygon overlaps).</param>
/// <param name="OverlapGeometry">The overlapping region as GeoJSON, if applicable.</param>
public sealed record SpatialConflictResult(
    int ExistingGeometryIndex,
    double OverlapAreaSquareMeters,
    GeoJsonGeometry? OverlapGeometry);

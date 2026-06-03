namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Detects spatial overlaps between a candidate geometry and a set of existing geometries.
/// Used by the Resource Engine to prevent booking conflicts on spatial resources (roads, parking, zones).
/// </summary>
public interface ISpatialConflictDetector
{
    /// <summary>
    /// Checks <paramref name="candidate"/> against each geometry in <paramref name="existingGeometries"/>
    /// and returns a conflict result for every pair that intersects.
    /// </summary>
    /// <returns>Empty if no conflicts; one entry per overlapping existing geometry.</returns>
    IReadOnlyList<SpatialConflictResult> DetectOverlaps(
        GeoJsonGeometry candidate,
        IReadOnlyList<GeoJsonGeometry> existingGeometries);
}

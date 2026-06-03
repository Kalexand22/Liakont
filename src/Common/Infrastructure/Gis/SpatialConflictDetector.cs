namespace Stratum.Common.Infrastructure.Gis;

using Stratum.Common.Abstractions.Gis;

/// <summary>
/// Detects spatial overlaps between a candidate geometry and existing geometries using NTS.
/// </summary>
internal sealed class SpatialConflictDetector : ISpatialConflictDetector
{
    public IReadOnlyList<SpatialConflictResult> DetectOverlaps(
        GeoJsonGeometry candidate,
        IReadOnlyList<GeoJsonGeometry> existingGeometries)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existingGeometries);

        if (existingGeometries.Count == 0)
        {
            return [];
        }

        var candidateNts = GeoJsonService.ToNts(candidate);
        var conflicts = new List<SpatialConflictResult>();

        for (int i = 0; i < existingGeometries.Count; i++)
        {
            var existingNts = GeoJsonService.ToNts(existingGeometries[i]);

            if (!candidateNts.Intersects(existingNts))
            {
                continue;
            }

            var intersection = candidateNts.Intersection(existingNts);
            var overlapArea = intersection.Area;

            GeoJsonGeometry? overlapGeometry = null;
            if (!intersection.IsEmpty)
            {
                overlapGeometry = GeoJsonService.FromNts(intersection);
            }

            conflicts.Add(new SpatialConflictResult(i, overlapArea, overlapGeometry));
        }

        return conflicts;
    }
}

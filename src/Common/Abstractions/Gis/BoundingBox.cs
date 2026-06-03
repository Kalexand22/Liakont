namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Axis-aligned bounding box for a geometry (WGS84 coordinates).
/// </summary>
public sealed record BoundingBox(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY);

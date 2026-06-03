namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// A single WFS feature type advertised in GetCapabilities.
/// </summary>
public sealed record WfsFeatureType
{
    public required string Name { get; init; }

    public required string Title { get; init; }

    public string? DefaultSrs { get; init; }

    public BoundingBox? BoundingBox { get; init; }
}

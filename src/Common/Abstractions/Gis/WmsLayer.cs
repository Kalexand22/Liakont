namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// A single WMS layer advertised in GetCapabilities.
/// </summary>
public sealed record WmsLayer
{
    public required string Name { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<string> Srs { get; init; } = [];

    public BoundingBox? BoundingBox { get; init; }
}

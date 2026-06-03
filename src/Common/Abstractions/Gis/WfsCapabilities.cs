namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Parsed WFS GetCapabilities response.
/// </summary>
public sealed record WfsCapabilities
{
    public required string ServiceTitle { get; init; }

    public required IReadOnlyList<WfsFeatureType> FeatureTypes { get; init; }
}

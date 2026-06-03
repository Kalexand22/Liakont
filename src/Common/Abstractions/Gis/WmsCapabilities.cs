namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Parsed WMS GetCapabilities response.
/// </summary>
public sealed record WmsCapabilities
{
    public required string ServiceTitle { get; init; }

    public required IReadOnlyList<WmsLayer> Layers { get; init; }
}

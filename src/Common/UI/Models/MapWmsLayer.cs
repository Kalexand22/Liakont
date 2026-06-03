namespace Stratum.Common.UI.Models;

/// <summary>
/// Describes a WMS overlay layer for StratumMap.
/// </summary>
public sealed record MapWmsLayer(
    string Name,
    string Url,
    string Layers,
    string? Format = "image/png",
    bool Transparent = true,
    double Opacity = 1.0);

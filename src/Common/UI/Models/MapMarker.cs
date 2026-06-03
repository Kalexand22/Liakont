namespace Stratum.Common.UI.Models;

/// <summary>
/// A marker to display on a StratumMap.
/// </summary>
/// <param name="Latitude">Marker latitude (WGS84).</param>
/// <param name="Longitude">Marker longitude (WGS84).</param>
/// <param name="Label">Optional text label shown on hover.</param>
/// <param name="PopupHtml">
/// Optional HTML content for the popup. SECURITY: this value is rendered as raw HTML
/// in the Leaflet popup. Callers MUST sanitize any user-supplied content before passing
/// it here. Only use trusted, server-controlled strings.
/// </param>
public sealed record MapMarker(
    double Latitude,
    double Longitude,
    string? Label = null,
    string? PopupHtml = null);

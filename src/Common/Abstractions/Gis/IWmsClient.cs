namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Client for OGC Web Map Service (WMS) operations.
/// </summary>
public interface IWmsClient
{
    /// <summary>
    /// Retrieves and parses the WMS GetCapabilities document.
    /// Results are cached per <see cref="GisOptions.CapabilitiesCacheTtlMinutes"/>.
    /// </summary>
    Task<WmsCapabilities> GetCapabilitiesAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>
    /// Requests a map image from the WMS service.
    /// </summary>
    /// <param name="baseUrl">WMS service base URL.</param>
    /// <param name="layers">Layer names to render.</param>
    /// <param name="bbox">Bounding box (minX,minY,maxX,maxY).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="format">Image format (e.g. "image/png"). Defaults to "image/png".</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw image bytes.</returns>
    Task<byte[]> GetMapAsync(
        string baseUrl,
        IReadOnlyList<string> layers,
        BoundingBox bbox,
        int width,
        int height,
        string format = "image/png",
        CancellationToken ct = default);
}

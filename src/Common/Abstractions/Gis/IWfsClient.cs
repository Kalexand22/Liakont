namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Client for OGC Web Feature Service (WFS) operations.
/// </summary>
public interface IWfsClient
{
    /// <summary>
    /// Retrieves and parses the WFS GetCapabilities document.
    /// Results are cached per <see cref="GisOptions.CapabilitiesCacheTtlMinutes"/>.
    /// </summary>
    Task<WfsCapabilities> GetCapabilitiesAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>
    /// Retrieves features from the WFS service as a GeoJSON FeatureCollection.
    /// </summary>
    /// <param name="baseUrl">WFS service base URL.</param>
    /// <param name="typeName">Feature type name.</param>
    /// <param name="bbox">Optional bounding box filter.</param>
    /// <param name="filter">Optional CQL/OGC filter expression.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<GeoFeatureSet> GetFeatureAsync(
        string baseUrl,
        string typeName,
        BoundingBox? bbox = null,
        string? filter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the schema description for a feature type.
    /// </summary>
    Task<FeatureTypeDescription> DescribeFeatureTypeAsync(
        string baseUrl,
        string typeName,
        CancellationToken ct = default);
}

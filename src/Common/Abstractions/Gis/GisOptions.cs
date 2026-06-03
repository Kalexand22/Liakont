namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// Configuration options for OGC WMS/WFS services.
/// Bound from the "Gis" section in appsettings.json (per-tenant via Config module).
/// </summary>
public sealed class GisOptions
{
    public const string SectionName = "Gis";

    /// <summary>Base URL for the WMS service (e.g. https://geoserver.example.com/wms).</summary>
    public string? WmsBaseUrl { get; set; }

    /// <summary>Base URL for the WFS service (e.g. https://geoserver.example.com/wfs).</summary>
    public string? WfsBaseUrl { get; set; }

    /// <summary>Default spatial reference system (e.g. "EPSG:4326"). Defaults to EPSG:4326.</summary>
    public string DefaultSrs { get; set; } = "EPSG:4326";

    /// <summary>Optional API key appended to OGC requests.</summary>
    public string? ApiKey { get; set; }

    /// <summary>HTTP request timeout in seconds. Defaults to 30.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum number of automatic retries on transient HTTP failures. Defaults to 2.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Cache TTL for GetCapabilities responses in minutes. Defaults to 60.</summary>
    public int CapabilitiesCacheTtlMinutes { get; set; } = 60;
}

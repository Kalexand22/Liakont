namespace Stratum.Common.Infrastructure.Gis;

using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Caching;
using Stratum.Common.Abstractions.Gis;

/// <summary>
/// OGC WMS client using IHttpClientFactory for managed connections.
/// Caches GetCapabilities responses via ICacheService.
/// </summary>
internal sealed partial class WmsClient : IWmsClient
{
    internal const string HttpClientName = "StratumWms";

    private const string CacheKeyPrefix = "gis:wms:capabilities:";

    private static readonly XNamespace Wms = "http://www.opengis.net/wms";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheService _cacheService;
    private readonly IOptions<GisOptions> _options;
    private readonly ILogger<WmsClient> _logger;

    public WmsClient(
        IHttpClientFactory httpClientFactory,
        ICacheService cacheService,
        IOptions<GisOptions> options,
        ILogger<WmsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cacheService = cacheService;
        _options = options;
        _logger = logger;
    }

    public async Task<WmsCapabilities> GetCapabilitiesAsync(string baseUrl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var cacheKey = CacheKeyPrefix + baseUrl;
        var cached = await _cacheService.GetAsync<WmsCapabilities>(cacheKey, ct);
        if (cached is not null)
        {
            return cached;
        }

        var url = BuildUrl(baseUrl, "GetCapabilities", []);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var responseXml = await client.GetStringAsync(url, ct);

        var capabilities = ParseCapabilities(responseXml);

        var ttl = TimeSpan.FromMinutes(_options.Value.CapabilitiesCacheTtlMinutes);
        await _cacheService.SetAsync(cacheKey, capabilities, ttl, ct);

        LogCapabilitiesLoaded(_logger, baseUrl, capabilities.Layers.Count);

        return capabilities;
    }

    public async Task<byte[]> GetMapAsync(
        string baseUrl,
        IReadOnlyList<string> layers,
        BoundingBox bbox,
        int width,
        int height,
        string format = "image/png",
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(bbox);

        var srs = _options.Value.DefaultSrs;
        var bboxStr = $"{bbox.MinX},{bbox.MinY},{bbox.MaxX},{bbox.MaxY}";

        var parameters = new Dictionary<string, string>
        {
            ["LAYERS"] = string.Join(",", layers),
            ["BBOX"] = bboxStr,
            ["WIDTH"] = width.ToString(CultureInfo.InvariantCulture),
            ["HEIGHT"] = height.ToString(CultureInfo.InvariantCulture),
            ["FORMAT"] = format,
            ["SRS"] = srs,
            ["STYLES"] = string.Empty,
        };

        var url = BuildUrl(baseUrl, "GetMap", parameters);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        return await client.GetByteArrayAsync(url, ct);
    }

    internal static WmsCapabilities ParseCapabilities(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;

        // Handle both namespaced (1.3.0) and non-namespaced (1.1.1) responses
        var serviceTitle = root.Element(Wms + "Service")?.Element(Wms + "Title")?.Value
            ?? root.Element("Service")?.Element("Title")?.Value
            ?? string.Empty;

        var layers = new List<WmsLayer>();

        var layerElements = root.Descendants(Wms + "Layer")
            .Concat(root.Descendants("Layer"));

        foreach (var layerEl in layerElements)
        {
            var name = layerEl.Element(Wms + "Name")?.Value
                ?? layerEl.Element("Name")?.Value;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var title = layerEl.Element(Wms + "Title")?.Value
                ?? layerEl.Element("Title")?.Value
                ?? name;

            var srs = layerEl.Elements(Wms + "CRS")
                .Concat(layerEl.Elements("SRS"))
                .Concat(layerEl.Elements(Wms + "SRS"))
                .Select(e => e.Value)
                .Distinct()
                .ToList();

            var bboxEl = layerEl.Element(Wms + "EX_GeographicBoundingBox")
                ?? layerEl.Element("LatLonBoundingBox");

            BoundingBox? bbox = null;
            if (bboxEl is not null)
            {
                bbox = ParseBoundingBox(bboxEl);
            }

            layers.Add(new WmsLayer
            {
                Name = name,
                Title = title,
                Srs = srs,
                BoundingBox = bbox,
            });
        }

        return new WmsCapabilities
        {
            ServiceTitle = serviceTitle,
            Layers = layers,
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "WMS GetCapabilities from {Url}: {LayerCount} layers")]
    private static partial void LogCapabilitiesLoaded(ILogger logger, string url, int layerCount);

    private static BoundingBox? ParseBoundingBox(XElement el)
    {
        // WMS 1.3.0 format: EX_GeographicBoundingBox
        var westEl = el.Element(Wms + "westBoundLongitude") ?? el.Element("westBoundLongitude");
        if (westEl is not null)
        {
            if (double.TryParse(westEl.Value, CultureInfo.InvariantCulture, out var west) &&
                double.TryParse((el.Element(Wms + "southBoundLatitude") ?? el.Element("southBoundLatitude"))?.Value, CultureInfo.InvariantCulture, out var south) &&
                double.TryParse((el.Element(Wms + "eastBoundLongitude") ?? el.Element("eastBoundLongitude"))?.Value, CultureInfo.InvariantCulture, out var east) &&
                double.TryParse((el.Element(Wms + "northBoundLatitude") ?? el.Element("northBoundLatitude"))?.Value, CultureInfo.InvariantCulture, out var north))
            {
                return new BoundingBox(west, south, east, north);
            }
        }

        // WMS 1.1.1 format: LatLonBoundingBox with attributes
        var minx = el.Attribute("minx")?.Value;
        if (minx is not null &&
            double.TryParse(minx, CultureInfo.InvariantCulture, out var minX) &&
            double.TryParse(el.Attribute("miny")?.Value, CultureInfo.InvariantCulture, out var minY) &&
            double.TryParse(el.Attribute("maxx")?.Value, CultureInfo.InvariantCulture, out var maxX) &&
            double.TryParse(el.Attribute("maxy")?.Value, CultureInfo.InvariantCulture, out var maxY))
        {
            return new BoundingBox(minX, minY, maxX, maxY);
        }

        return null;
    }

    private string BuildUrl(string baseUrl, string request, Dictionary<string, string> extraParams)
    {
        var separator = baseUrl.Contains('?') ? "&" : "?";
        var queryParts = new List<string>
        {
            "SERVICE=WMS",
            "VERSION=1.3.0",
            $"REQUEST={request}",
        };

        foreach (var (key, value) in extraParams)
        {
            queryParts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        var apiKey = _options.Value.ApiKey;
        if (!string.IsNullOrEmpty(apiKey))
        {
            queryParts.Add($"API_KEY={Uri.EscapeDataString(apiKey)}");
        }

        return $"{baseUrl}{separator}{string.Join("&", queryParts)}";
    }
}

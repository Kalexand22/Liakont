namespace Stratum.Common.Infrastructure.Gis;

using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Caching;
using Stratum.Common.Abstractions.Gis;

/// <summary>
/// OGC WFS client using IHttpClientFactory for managed connections.
/// Caches GetCapabilities responses via ICacheService.
/// </summary>
internal sealed partial class WfsClient : IWfsClient
{
    internal const string HttpClientName = "StratumWfs";
    private const string CacheKeyPrefix = "gis:wfs:capabilities:";

    private static readonly XNamespace Wfs = "http://www.opengis.net/wfs/2.0";
    private static readonly XNamespace WfsLegacy = "http://www.opengis.net/wfs";
    private static readonly XNamespace Ows = "http://www.opengis.net/ows/1.1";
    private static readonly XNamespace OwsLegacy = "http://www.opengis.net/ows";
    private static readonly XNamespace Xsd = "http://www.w3.org/2001/XMLSchema";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheService _cacheService;
    private readonly IOptions<GisOptions> _options;
    private readonly ILogger<WfsClient> _logger;

    public WfsClient(
        IHttpClientFactory httpClientFactory,
        ICacheService cacheService,
        IOptions<GisOptions> options,
        ILogger<WfsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cacheService = cacheService;
        _options = options;
        _logger = logger;
    }

    public async Task<WfsCapabilities> GetCapabilitiesAsync(string baseUrl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var cacheKey = CacheKeyPrefix + baseUrl;
        var cached = await _cacheService.GetAsync<WfsCapabilities>(cacheKey, ct);
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

        LogCapabilitiesLoaded(_logger, baseUrl, capabilities.FeatureTypes.Count);

        return capabilities;
    }

    public async Task<GeoFeatureSet> GetFeatureAsync(
        string baseUrl,
        string typeName,
        BoundingBox? bbox = null,
        string? filter = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        var parameters = new Dictionary<string, string>
        {
            ["TYPENAMES"] = typeName,
            ["OUTPUTFORMAT"] = "application/json",
            ["SRSNAME"] = _options.Value.DefaultSrs,
        };

        if (bbox is not null)
        {
            parameters["BBOX"] = $"{bbox.MinX},{bbox.MinY},{bbox.MaxX},{bbox.MaxY},{_options.Value.DefaultSrs}";
        }

        if (!string.IsNullOrEmpty(filter))
        {
            parameters["CQL_FILTER"] = filter;
        }

        var url = BuildUrl(baseUrl, "GetFeature", parameters);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var responseJson = await client.GetStringAsync(url, ct);

        return ParseFeatureCollection(responseJson);
    }

    public async Task<FeatureTypeDescription> DescribeFeatureTypeAsync(
        string baseUrl,
        string typeName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        var parameters = new Dictionary<string, string>
        {
            ["TYPENAMES"] = typeName,
        };

        var url = BuildUrl(baseUrl, "DescribeFeatureType", parameters);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var responseXml = await client.GetStringAsync(url, ct);

        return ParseFeatureTypeDescription(responseXml, typeName);
    }

    internal static WfsCapabilities ParseCapabilities(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;

        var serviceTitle =
            root.Element(Ows + "ServiceIdentification")?.Element(Ows + "Title")?.Value
            ?? root.Element(OwsLegacy + "ServiceIdentification")?.Element(OwsLegacy + "Title")?.Value
            ?? root.Element("Service")?.Element("Title")?.Value
            ?? string.Empty;

        var featureTypes = new List<WfsFeatureType>();

        var typeListElements = root.Descendants(Wfs + "FeatureType")
            .Concat(root.Descendants(WfsLegacy + "FeatureType"))
            .Concat(root.Descendants("FeatureType"));

        foreach (var ftEl in typeListElements)
        {
            var name = ftEl.Element(Wfs + "Name")?.Value
                ?? ftEl.Element(WfsLegacy + "Name")?.Value
                ?? ftEl.Element("Name")?.Value;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var title = ftEl.Element(Wfs + "Title")?.Value
                ?? ftEl.Element(WfsLegacy + "Title")?.Value
                ?? ftEl.Element("Title")?.Value
                ?? name;

            var defaultSrs = ftEl.Element(Wfs + "DefaultCRS")?.Value
                ?? ftEl.Element(WfsLegacy + "DefaultSRS")?.Value
                ?? ftEl.Element("DefaultSRS")?.Value;

            var bboxEl = ftEl.Element(Ows + "WGS84BoundingBox")
                ?? ftEl.Element(OwsLegacy + "WGS84BoundingBox");

            BoundingBox? bbox = null;
            if (bboxEl is not null)
            {
                bbox = ParseOwsBoundingBox(bboxEl);
            }

            featureTypes.Add(new WfsFeatureType
            {
                Name = name,
                Title = title,
                DefaultSrs = defaultSrs,
                BoundingBox = bbox,
            });
        }

        return new WfsCapabilities
        {
            ServiceTitle = serviceTitle,
            FeatureTypes = featureTypes,
        };
    }

    internal static GeoFeatureSet ParseFeatureCollection(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var features = new List<GeoFeature>();

        if (root.TryGetProperty("features", out var featuresArray))
        {
            foreach (var featureEl in featuresArray.EnumerateArray())
            {
                var id = featureEl.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;

                GeoJsonGeometry? geometry = null;
                if (featureEl.TryGetProperty("geometry", out var geomEl) &&
                    geomEl.ValueKind != JsonValueKind.Null)
                {
                    var geomJson = geomEl.GetRawText();
                    var geomType = geomEl.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "Unknown" : "Unknown";
                    geometry = new GeoJsonGeometry(geomJson, geomType);
                }

                var properties = new Dictionary<string, object?>();
                if (featureEl.TryGetProperty("properties", out var propsEl) &&
                    propsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in propsEl.EnumerateObject())
                    {
                        properties[prop.Name] = ConvertJsonElement(prop.Value);
                    }
                }

                features.Add(new GeoFeature
                {
                    Id = id,
                    Geometry = geometry,
                    Properties = properties,
                });
            }
        }

        return new GeoFeatureSet { Features = features };
    }

    internal static FeatureTypeDescription ParseFeatureTypeDescription(string xml, string typeName)
    {
        var doc = XDocument.Parse(xml);
        var properties = new List<FeatureProperty>();

        var elements = doc.Descendants(Xsd + "element");

        foreach (var el in elements)
        {
            var name = el.Attribute("name")?.Value;
            var type = el.Attribute("type")?.Value;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type))
            {
                continue;
            }

            // Skip the top-level element that represents the feature type itself
            if (name == typeName || name.EndsWith($":{typeName}", StringComparison.Ordinal))
            {
                continue;
            }

            var nillable = el.Attribute("nillable")?.Value;
            var minOccurs = el.Attribute("minOccurs")?.Value;
            var isNullable = nillable != "false" && minOccurs != "1";

            properties.Add(new FeatureProperty
            {
                Name = name,
                Type = NormalizeXsdType(type),
                IsNullable = isNullable,
            });
        }

        return new FeatureTypeDescription
        {
            TypeName = typeName,
            Properties = properties,
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "WFS GetCapabilities from {Url}: {TypeCount} feature types")]
    private static partial void LogCapabilitiesLoaded(ILogger logger, string url, int typeCount);

    private static BoundingBox? ParseOwsBoundingBox(XElement el)
    {
        var lowerCorner = (el.Element(Ows + "LowerCorner") ?? el.Element(OwsLegacy + "LowerCorner"))?.Value;
        var upperCorner = (el.Element(Ows + "UpperCorner") ?? el.Element(OwsLegacy + "UpperCorner"))?.Value;

        if (lowerCorner is null || upperCorner is null)
        {
            return null;
        }

        var lower = lowerCorner.Split(' ');
        var upper = upperCorner.Split(' ');

        if (lower.Length >= 2 && upper.Length >= 2 &&
            double.TryParse(lower[0], System.Globalization.CultureInfo.InvariantCulture, out var minX) &&
            double.TryParse(lower[1], System.Globalization.CultureInfo.InvariantCulture, out var minY) &&
            double.TryParse(upper[0], System.Globalization.CultureInfo.InvariantCulture, out var maxX) &&
            double.TryParse(upper[1], System.Globalization.CultureInfo.InvariantCulture, out var maxY))
        {
            return new BoundingBox(minX, minY, maxX, maxY);
        }

        return null;
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText(),
    };

    private static string NormalizeXsdType(string xsdType)
    {
        // Remove namespace prefix (e.g. "xsd:string" -> "string")
        var colonIdx = xsdType.IndexOf(':');
        var localType = colonIdx >= 0 ? xsdType[(colonIdx + 1)..] : xsdType;

        return localType switch
        {
            "string" => "string",
            "int" or "integer" or "long" or "short" => "integer",
            "double" or "float" or "decimal" => "double",
            "boolean" => "boolean",
            "date" or "dateTime" => "datetime",
            _ when localType.Contains("Geometry", StringComparison.OrdinalIgnoreCase) => "geometry",
            _ when localType.Contains("Point", StringComparison.OrdinalIgnoreCase) => "geometry",
            _ when localType.Contains("Polygon", StringComparison.OrdinalIgnoreCase) => "geometry",
            _ when localType.Contains("Line", StringComparison.OrdinalIgnoreCase) => "geometry",
            _ => localType,
        };
    }

    private string BuildUrl(string baseUrl, string request, Dictionary<string, string> extraParams)
    {
        var separator = baseUrl.Contains('?') ? "&" : "?";
        var queryParts = new List<string>
        {
            "SERVICE=WFS",
            "VERSION=2.0.0",
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

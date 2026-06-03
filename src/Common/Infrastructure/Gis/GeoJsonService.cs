namespace Stratum.Common.Infrastructure.Gis;

using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using Stratum.Common.Abstractions.Gis;

/// <summary>
/// GeoJSON parsing, validation, and spatial computation service backed by NetTopologySuite.
/// Uses System.Text.Json via NetTopologySuite.IO.GeoJSON4STJ (no Newtonsoft dependency).
/// </summary>
internal sealed class GeoJsonService : IGeoJsonService
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public GeoJsonGeometry Parse(string geoJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(geoJson);

        Geometry? geometry;
        try
        {
            geometry = JsonSerializer.Deserialize<Geometry>(geoJson, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Invalid GeoJSON: {ex.Message}", ex);
        }

        if (geometry is null)
        {
            throw new FormatException("GeoJSON parsed to null geometry.");
        }

        return new GeoJsonGeometry(geoJson, geometry.GeometryType);
    }

    public bool Validate(GeoJsonGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        try
        {
            var ntsGeom = ToNts(geometry);
            return ntsGeom.IsValid;
        }
        catch
        {
            return false;
        }
    }

    public BoundingBox CalculateBoundingBox(GeoJsonGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var ntsGeom = ToNts(geometry);
        var envelope = ntsGeom.EnvelopeInternal;
        return new BoundingBox(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
    }

    public bool Intersects(GeoJsonGeometry geometry1, GeoJsonGeometry geometry2)
    {
        ArgumentNullException.ThrowIfNull(geometry1);
        ArgumentNullException.ThrowIfNull(geometry2);

        var g1 = ToNts(geometry1);
        var g2 = ToNts(geometry2);
        return g1.Intersects(g2);
    }

    public bool Contains(GeoJsonGeometry geometry, GeoJsonGeometry point)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(point);

        var g = ToNts(geometry);
        var p = ToNts(point);
        return g.Contains(p);
    }

    public double Area(GeoJsonGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var ntsGeom = ToNts(geometry);
        return ntsGeom.Area;
    }

    public string Serialize(GeoJsonGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return geometry.GeoJson;
    }

    internal static Geometry ToNts(GeoJsonGeometry geometry)
    {
        return JsonSerializer.Deserialize<Geometry>(geometry.GeoJson, SerializerOptions)
            ?? throw new FormatException("GeoJSON deserialized to null.");
    }

    internal static GeoJsonGeometry FromNts(Geometry ntsGeometry)
    {
        var json = JsonSerializer.Serialize(ntsGeometry, SerializerOptions);
        return new GeoJsonGeometry(json, ntsGeometry.GeometryType);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());
        return options;
    }
}

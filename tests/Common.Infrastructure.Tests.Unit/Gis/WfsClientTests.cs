namespace Stratum.Common.Infrastructure.Tests.Unit.Gis;

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Gis;
using Stratum.Common.Infrastructure.Gis;
using Xunit;
using static WmsClientTests;

public sealed class WfsClientTests
{
    private const string BaseUrl = "https://geoserver.example.com/wfs";

    [Fact]
    public async Task GetCapabilities_Parses_WFS20_Response()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <wfs:WFS_Capabilities version="2.0.0"
              xmlns:wfs="http://www.opengis.net/wfs/2.0"
              xmlns:ows="http://www.opengis.net/ows/1.1">
              <ows:ServiceIdentification>
                <ows:Title>Test WFS</ows:Title>
              </ows:ServiceIdentification>
              <wfs:FeatureTypeList>
                <wfs:FeatureType>
                  <wfs:Name>parcels</wfs:Name>
                  <wfs:Title>Cadastral Parcels</wfs:Title>
                  <wfs:DefaultCRS>urn:ogc:def:crs:EPSG::4326</wfs:DefaultCRS>
                  <ows:WGS84BoundingBox>
                    <ows:LowerCorner>-5.0 41.0</ows:LowerCorner>
                    <ows:UpperCorner>10.0 51.0</ows:UpperCorner>
                  </ows:WGS84BoundingBox>
                </wfs:FeatureType>
                <wfs:FeatureType>
                  <wfs:Name>roads</wfs:Name>
                  <wfs:Title>Road Network</wfs:Title>
                  <wfs:DefaultCRS>urn:ogc:def:crs:EPSG::4326</wfs:DefaultCRS>
                </wfs:FeatureType>
              </wfs:FeatureTypeList>
            </wfs:WFS_Capabilities>
            """;

        var handler = new FakeHttpMessageHandler(xml, "application/xml");
        var sut = CreateClient(handler);

        var result = await sut.GetCapabilitiesAsync(BaseUrl);

        result.ServiceTitle.Should().Be("Test WFS");
        result.FeatureTypes.Should().HaveCount(2);
        result.FeatureTypes[0].Name.Should().Be("parcels");
        result.FeatureTypes[0].DefaultSrs.Should().Be("urn:ogc:def:crs:EPSG::4326");
        result.FeatureTypes[0].BoundingBox.Should().NotBeNull();
        result.FeatureTypes[0].BoundingBox!.MinX.Should().Be(-5.0);
        result.FeatureTypes[1].Name.Should().Be("roads");
    }

    [Fact]
    public async Task GetCapabilities_Returns_Cached_Result_On_Second_Call()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <wfs:WFS_Capabilities version="2.0.0"
              xmlns:wfs="http://www.opengis.net/wfs/2.0"
              xmlns:ows="http://www.opengis.net/ows/1.1">
              <ows:ServiceIdentification><ows:Title>Test</ows:Title></ows:ServiceIdentification>
              <wfs:FeatureTypeList>
                <wfs:FeatureType><wfs:Name>ft1</wfs:Name><wfs:Title>FT1</wfs:Title></wfs:FeatureType>
              </wfs:FeatureTypeList>
            </wfs:WFS_Capabilities>
            """;

        var handler = new FakeHttpMessageHandler(xml, "application/xml");
        var cache = new FakeCacheService();
        var sut = CreateClient(handler, cache);

        await sut.GetCapabilitiesAsync(BaseUrl);
        await sut.GetCapabilitiesAsync(BaseUrl);

        handler.CallCount.Should().Be(1, "second call should hit cache");
    }

    [Fact]
    public async Task GetFeature_Parses_GeoJSON_GeoFeatureSet()
    {
        var json = """
            {
              "type": "GeoFeatureSet",
              "features": [
                {
                  "type": "Feature",
                  "id": "parcels.1",
                  "geometry": {"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,1],[0,0]]]},
                  "properties": {"name": "Parcel A", "area": 1000, "public": true}
                },
                {
                  "type": "Feature",
                  "id": "parcels.2",
                  "geometry": null,
                  "properties": {"name": "Parcel B", "area": null}
                }
              ]
            }
            """;

        var handler = new FakeHttpMessageHandler(json, "application/json");
        var sut = CreateClient(handler);

        var result = await sut.GetFeatureAsync(BaseUrl, "parcels");

        result.Features.Should().HaveCount(2);
        result.Features[0].Id.Should().Be("parcels.1");
        result.Features[0].Geometry.Should().NotBeNull();
        result.Features[0].Geometry!.Type.Should().Be("Polygon");
        result.Features[0].Properties["name"].Should().Be("Parcel A");
        result.Features[0].Properties["area"].Should().Be(1000L);
        result.Features[0].Properties["public"].Should().Be(true);
        result.Features[1].Geometry.Should().BeNull();
        result.Features[1].Properties["area"].Should().BeNull();
    }

    [Fact]
    public async Task GetFeature_Sends_Bbox_Parameter()
    {
        var json = """{"type":"GeoFeatureSet","features":[]}""";
        var handler = new FakeHttpMessageHandler(json, "application/json");
        var sut = CreateClient(handler);

        var bbox = new BoundingBox(-5.0, 41.0, 10.0, 51.0);
        await sut.GetFeatureAsync(BaseUrl, "roads", bbox);

        var url = handler.LastRequestUri!.ToString();
        url.Should().Contain("SERVICE=WFS");
        url.Should().Contain("REQUEST=GetFeature");
        url.Should().Contain("TYPENAMES=roads");
        url.Should().Contain("BBOX=");
    }

    [Fact]
    public async Task GetFeature_Sends_CqlFilter_Parameter()
    {
        var json = """{"type":"GeoFeatureSet","features":[]}""";
        var handler = new FakeHttpMessageHandler(json, "application/json");
        var sut = CreateClient(handler);

        await sut.GetFeatureAsync(BaseUrl, "parcels", filter: "area > 500");

        handler.LastRequestUri!.ToString().Should().Contain("CQL_FILTER=");
    }

    [Fact]
    public async Task DescribeFeatureType_Parses_XSD_Response()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <xsd:schema xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                        targetNamespace="http://example.com/parcels">
              <xsd:complexType name="parcelsType">
                <xsd:complexContent>
                  <xsd:extension base="gml:AbstractFeatureType">
                    <xsd:sequence>
                      <xsd:element name="name" type="xsd:string" nillable="true" minOccurs="0"/>
                      <xsd:element name="area" type="xsd:double" nillable="false" minOccurs="1"/>
                      <xsd:element name="geom" type="gml:PolygonPropertyType" nillable="true" minOccurs="0"/>
                      <xsd:element name="created" type="xsd:dateTime" nillable="true"/>
                    </xsd:sequence>
                  </xsd:extension>
                </xsd:complexContent>
              </xsd:complexType>
            </xsd:schema>
            """;

        var handler = new FakeHttpMessageHandler(xml, "application/xml");
        var sut = CreateClient(handler);

        var result = await sut.DescribeFeatureTypeAsync(BaseUrl, "parcels");

        result.TypeName.Should().Be("parcels");
        result.Properties.Should().HaveCount(4);
        result.Properties[0].Name.Should().Be("name");
        result.Properties[0].Type.Should().Be("string");
        result.Properties[0].IsNullable.Should().BeTrue();
        result.Properties[1].Name.Should().Be("area");
        result.Properties[1].Type.Should().Be("double");
        result.Properties[1].IsNullable.Should().BeFalse();
        result.Properties[2].Name.Should().Be("geom");
        result.Properties[2].Type.Should().Be("geometry");
        result.Properties[3].Name.Should().Be("created");
        result.Properties[3].Type.Should().Be("datetime");
    }

    [Fact]
    public void ParseGeoFeatureSet_Empty_Features()
    {
        var json = """{"type":"GeoFeatureSet","features":[]}""";

        var result = WfsClient.ParseFeatureCollection(json);

        result.Features.Should().BeEmpty();
    }

    private static WfsClient CreateClient(
        FakeHttpMessageHandler handler,
        FakeCacheService? cache = null,
        GisOptions? options = null)
    {
        options ??= new GisOptions();
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        return new WfsClient(
            factory,
            cache ?? new FakeCacheService(),
            Options.Create(options),
            NullLogger<WfsClient>.Instance);
    }
}

namespace Stratum.Common.Infrastructure.Tests.Unit.Gis;

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Caching;
using Stratum.Common.Abstractions.Gis;
using Stratum.Common.Infrastructure.Gis;
using Xunit;

public sealed class WmsClientTests
{
    private const string BaseUrl = "https://geoserver.example.com/wms";

    [Fact]
    public async Task GetCapabilities_Parses_WMS130_Response()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <WMS_Capabilities version="1.3.0" xmlns="http://www.opengis.net/wms">
              <Service><Title>Test WMS</Title></Service>
              <Capability>
                <Layer>
                  <Layer queryable="1">
                    <Name>roads</Name>
                    <Title>Road Network</Title>
                    <CRS>EPSG:4326</CRS>
                    <CRS>EPSG:3857</CRS>
                    <EX_GeographicBoundingBox>
                      <westBoundLongitude>-5.0</westBoundLongitude>
                      <eastBoundLongitude>10.0</eastBoundLongitude>
                      <southBoundLatitude>41.0</southBoundLatitude>
                      <northBoundLatitude>51.0</northBoundLatitude>
                    </EX_GeographicBoundingBox>
                  </Layer>
                  <Layer queryable="1">
                    <Name>buildings</Name>
                    <Title>Buildings</Title>
                    <CRS>EPSG:4326</CRS>
                  </Layer>
                </Layer>
              </Capability>
            </WMS_Capabilities>
            """;

        var handler = new FakeHttpMessageHandler(xml, "application/xml");
        var sut = CreateClient(handler);

        var result = await sut.GetCapabilitiesAsync(BaseUrl);

        result.ServiceTitle.Should().Be("Test WMS");
        result.Layers.Should().HaveCount(2);
        result.Layers[0].Name.Should().Be("roads");
        result.Layers[0].Title.Should().Be("Road Network");
        result.Layers[0].Srs.Should().Contain("EPSG:4326");
        result.Layers[0].Srs.Should().Contain("EPSG:3857");
        result.Layers[0].BoundingBox.Should().NotBeNull();
        result.Layers[0].BoundingBox!.MinX.Should().Be(-5.0);
        result.Layers[1].Name.Should().Be("buildings");
    }

    [Fact]
    public async Task GetCapabilities_Returns_Cached_Result_On_Second_Call()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <WMS_Capabilities version="1.3.0" xmlns="http://www.opengis.net/wms">
              <Service><Title>Test WMS</Title></Service>
              <Capability>
                <Layer>
                  <Layer><Name>layer1</Name><Title>Layer 1</Title></Layer>
                </Layer>
              </Capability>
            </WMS_Capabilities>
            """;

        var handler = new FakeHttpMessageHandler(xml, "application/xml");
        var cache = new FakeCacheService();
        var sut = CreateClient(handler, cache);

        var result1 = await sut.GetCapabilitiesAsync(BaseUrl);
        var result2 = await sut.GetCapabilitiesAsync(BaseUrl);

        handler.CallCount.Should().Be(1, "second call should hit cache");
        result2.ServiceTitle.Should().Be(result1.ServiceTitle);
    }

    [Fact]
    public async Task GetMap_Sends_Correct_Parameters()
    {
        var handler = new FakeHttpMessageHandler([], "image/png");
        var sut = CreateClient(handler);

        var bbox = new BoundingBox(-5.0, 41.0, 10.0, 51.0);
        var result = await sut.GetMapAsync(BaseUrl, ["roads", "buildings"], bbox, 800, 600);

        result.Should().BeEmpty(); // Our fake returns empty bytes
        var requestUrl = handler.LastRequestUri!.ToString();
        requestUrl.Should().Contain("SERVICE=WMS");
        requestUrl.Should().Contain("REQUEST=GetMap");
        requestUrl.Should().Contain("LAYERS=roads%2Cbuildings");
        requestUrl.Should().Contain("WIDTH=800");
        requestUrl.Should().Contain("HEIGHT=600");
    }

    [Fact]
    public void ParseCapabilities_WMS111_LatLonBoundingBox()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <WMT_MS_Capabilities version="1.1.1">
              <Service><Title>Legacy WMS</Title></Service>
              <Capability>
                <Layer>
                  <Name>parcels</Name>
                  <Title>Cadastral Parcels</Title>
                  <SRS>EPSG:4326</SRS>
                  <LatLonBoundingBox minx="-5.0" miny="41.0" maxx="10.0" maxy="51.0"/>
                </Layer>
              </Capability>
            </WMT_MS_Capabilities>
            """;

        var result = WmsClient.ParseCapabilities(xml);

        result.ServiceTitle.Should().Be("Legacy WMS");
        result.Layers.Should().ContainSingle();
        result.Layers[0].Name.Should().Be("parcels");
        result.Layers[0].BoundingBox.Should().NotBeNull();
        result.Layers[0].BoundingBox!.MinX.Should().Be(-5.0);
        result.Layers[0].BoundingBox!.MaxY.Should().Be(51.0);
    }

    [Fact]
    public async Task GetCapabilities_Appends_ApiKey_When_Configured()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <WMS_Capabilities version="1.3.0" xmlns="http://www.opengis.net/wms">
              <Service><Title>Test</Title></Service>
              <Capability></Capability>
            </WMS_Capabilities>
            """;

        var handler = new FakeHttpMessageHandler(xml, "application/xml");
        var options = new GisOptions { ApiKey = "secret-key-123" };
        var sut = CreateClient(handler, options: options);

        await sut.GetCapabilitiesAsync(BaseUrl);

        handler.LastRequestUri!.ToString().Should().Contain("API_KEY=secret-key-123");
    }

    private static WmsClient CreateClient(
        FakeHttpMessageHandler handler,
        FakeCacheService? cache = null,
        GisOptions? options = null)
    {
        options ??= new GisOptions();
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        return new WmsClient(
            factory,
            cache ?? new FakeCacheService(),
            Options.Create(options),
            NullLogger<WmsClient>.Instance);
    }

    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _responseBytes;
        private readonly string _contentType;

        public FakeHttpMessageHandler(string responseBody, string contentType)
            : this(System.Text.Encoding.UTF8.GetBytes(responseBody), contentType)
        {
        }

        public FakeHttpMessageHandler(byte[] responseBytes, string contentType)
        {
            _responseBytes = responseBytes;
            _contentType = contentType;
        }

        public int CallCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_responseBytes),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_contentType);
            return Task.FromResult(response);
        }
    }

    internal sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FakeHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    internal sealed class FakeCacheService : ICacheService
    {
        private readonly Dictionary<string, object> _store = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
            where T : class =>
            Task.FromResult(_store.TryGetValue(key, out var val) ? val as T : null);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
            where T : class
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
        {
            var keys = _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var key in keys)
            {
                _store.Remove(key);
            }

            return Task.CompletedTask;
        }
    }
}

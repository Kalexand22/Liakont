namespace Stratum.Common.Infrastructure.Tests.Unit.Gis;

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Gis;
using Stratum.Common.Infrastructure.Gis;
using Xunit;

public sealed class RetryDelegatingHandlerTests
{
    [Fact]
    public async Task Returns_Immediately_On_Success()
    {
        var inner = new SequenceHandler([HttpStatusCode.OK]);
        var sut = CreateHandler(inner, maxRetries: 2);
        var client = new HttpClient(sut);

        var response = await client.GetAsync("https://example.com/wms");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Retries_On_ServiceUnavailable_Then_Succeeds()
    {
        var inner = new SequenceHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK]);
        var sut = CreateHandler(inner, maxRetries: 2);
        var client = new HttpClient(sut);

        var response = await client.GetAsync("https://example.com/wms");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Exhausts_Retries_And_Returns_Last_Response()
    {
        var inner = new SequenceHandler(
            [HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway]);
        var sut = CreateHandler(inner, maxRetries: 2);
        var client = new HttpClient(sut);

        var response = await client.GetAsync("https://example.com/wms");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        inner.CallCount.Should().Be(3); // 1 initial + 2 retries
    }

    [Fact]
    public async Task Does_Not_Retry_On_Non_Transient_Error()
    {
        var inner = new SequenceHandler([HttpStatusCode.NotFound]);
        var sut = CreateHandler(inner, maxRetries: 2);
        var client = new HttpClient(sut);

        var response = await client.GetAsync("https://example.com/wms");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Does_Not_Retry_On_BadRequest()
    {
        var inner = new SequenceHandler([HttpStatusCode.BadRequest]);
        var sut = CreateHandler(inner, maxRetries: 2);
        var client = new HttpClient(sut);

        var response = await client.GetAsync("https://example.com/wms");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Zero_MaxRetries_No_Retry()
    {
        var inner = new SequenceHandler([HttpStatusCode.ServiceUnavailable]);
        var sut = CreateHandler(inner, maxRetries: 0);
        var client = new HttpClient(sut);

        var response = await client.GetAsync("https://example.com/wms");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        inner.CallCount.Should().Be(1);
    }

    private static RetryDelegatingHandler CreateHandler(HttpMessageHandler inner, int maxRetries)
    {
        var options = Options.Create(new GisOptions { MaxRetries = maxRetries });
        var handler = new RetryDelegatingHandler(options, NullLogger<RetryDelegatingHandler>.Instance)
        {
            InnerHandler = inner,
        };
        return handler;
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _sequence;
        private int _index;

        public SequenceHandler(HttpStatusCode[] sequence) => _sequence = sequence;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var statusCode = _index < _sequence.Length ? _sequence[_index++] : _sequence[^1];
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(string.Empty),
            });
        }
    }
}

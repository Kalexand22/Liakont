namespace Stratum.Common.UI.Tests.Unit;

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.UI.Services.Filters;
using Xunit;

public sealed class OpenRouterGridFilterAIProviderTests : IDisposable
{
    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task CompleteAsyncShouldReturnUnavailableWhenApiKeyMissing()
    {
        var provider = BuildProvider(
            new GridFilterAIConfiguration { ApiKey = null },
            new StubHandler(_ => throw new InvalidOperationException("HTTP must not be called")));

        provider.IsAvailable.Should().BeFalse();

        var result = await provider.CompleteAsync("whatever");

        result.Success.Should().BeFalse();
        result.ResponseJson.Should().BeNull();
        result.Error.Should().Contain("not configured");
    }

    [Fact]
    public async Task CompleteAsyncShouldReturnSuccessWithBodyOn2xx()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "choices": [] }"""),
        });
        var provider = BuildProvider(
            new GridFilterAIConfiguration { ApiKey = "sk-test", TimeoutSeconds = 10 },
            handler);

        provider.IsAvailable.Should().BeTrue();

        var result = await provider.CompleteAsync("any prompt");

        result.Success.Should().BeTrue();
        result.ResponseJson.Should().Contain("choices");
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("sk-test");
    }

    [Fact]
    public async Task CompleteAsyncShouldMapNon2xxToErrorResponse()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad request body"),
        });
        var provider = BuildProvider(
            new GridFilterAIConfiguration { ApiKey = "sk-test", TimeoutSeconds = 10 },
            handler);

        var result = await provider.CompleteAsync("x");

        result.Success.Should().BeFalse();
        result.ResponseJson.Should().BeNull();
        result.Error.Should().Contain("400");
    }

    [Fact]
    public async Task CompleteAsyncShouldReturnTimeoutMessageWhenLinkedCtsFires()
    {
        var handler = new StubHandler(async (request, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = BuildProvider(
            new GridFilterAIConfiguration { ApiKey = "sk-test", TimeoutSeconds = 1 },
            handler);

        var result = await provider.CompleteAsync("x");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("temps");
    }

    [Fact]
    public async Task CompleteAsyncShouldPropagateCallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHandler(async (request, ct) =>
        {
            cts.Cancel();
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = BuildProvider(
            new GridFilterAIConfiguration { ApiKey = "sk-test", TimeoutSeconds = 10 },
            handler);

        var act = async () => await provider.CompleteAsync("x", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CompleteAsyncShouldClampNonPositiveTimeoutSecondsToDefault()
    {
        // A mis-configured 0-second timeout used to immediately cancel the call;
        // the provider clamps non-positive values to a sensible default.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        });
        var provider = BuildProvider(
            new GridFilterAIConfiguration { ApiKey = "sk-test", TimeoutSeconds = 0 },
            handler);

        var result = await provider.CompleteAsync("x");

        result.Success.Should().BeTrue();
    }

    private OpenRouterGridFilterAIProvider BuildProvider(
        GridFilterAIConfiguration config,
        HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        _clients.Add(client);
        return new OpenRouterGridFilterAIProvider(
            client,
            Options.Create(config),
            NullLogger<OpenRouterGridFilterAIProvider>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((req, _) => Task.FromResult(respond(req)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return await _respond(request, cancellationToken);
        }
    }
}

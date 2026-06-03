namespace Stratum.Common.Infrastructure.Tests.Unit.Keycloak;

/// <summary>
/// Fake IHttpClientFactory that returns HttpClients using a shared handler.
/// </summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public FakeHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name)
    {
        return new HttpClient(_handler, disposeHandler: false);
    }
}

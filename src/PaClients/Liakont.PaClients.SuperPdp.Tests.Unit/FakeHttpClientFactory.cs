namespace Liakont.PaClients.SuperPdp.Tests.Unit;

/// <summary>Fabrique de client HTTP de test : renvoie des <see cref="HttpClient"/> sur un handler partagé.</summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

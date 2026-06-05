namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Net;
using System.Text;

/// <summary>
/// Handler HTTP de test : enregistre la dernière requête (URI, en-têtes, CORPS) pour assertion du
/// payload « fil », et retourne une réponse fixée (code + corps) OU lève une exception fixée
/// (réseau/timeout). Aucune PA réelle n'est appelée — c'est le mock HTTP exigé par PAB01.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _responseBody;
    private readonly Exception? _throw;

    private StubHttpMessageHandler(HttpStatusCode status, string responseBody, Exception? toThrow)
    {
        _status = status;
        _responseBody = responseBody;
        _throw = toThrow;
    }

    public int CallCount { get; private set; }

    public Uri? LastRequestUri { get; private set; }

    public string? LastRequestBody { get; private set; }

    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>Répond avec un code HTTP et un corps JSON donnés.</summary>
    public static StubHttpMessageHandler Returns(HttpStatusCode status, string responseBody) =>
        new(status, responseBody, null);

    /// <summary>Lève l'exception donnée (simule réseau / timeout).</summary>
    public static StubHttpMessageHandler Throws(Exception toThrow) =>
        new(HttpStatusCode.OK, string.Empty, toThrow);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        LastRequestUri = request.RequestUri;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_throw is not null)
        {
            throw _throw;
        }

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
        };
    }
}

namespace Liakont.SignatureProviders.Yousign.Tests.Unit;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Double de test d'un <see cref="HttpMessageHandler"/> : renvoie des réponses scriptées dans l'ordre et
/// journalise les URI appelées (pour prouver le retry, l'anti-SSRF et la construction des appels).
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<Uri?> CalledUris { get; } = [];

    public void Enqueue(HttpStatusCode status, string body = "")
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
        });
    }

    public void EnqueueBytes(HttpStatusCode status, byte[] content, string contentType)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(content) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return response;
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CalledUris.Add(request.RequestUri);
        var factory = _responses.Count > 0
            ? _responses.Dequeue()
            : _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
        return Task.FromResult(factory(request));
    }
}

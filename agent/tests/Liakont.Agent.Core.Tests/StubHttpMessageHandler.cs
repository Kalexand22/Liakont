namespace Liakont.Agent.Core.Tests;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Gestionnaire HTTP mocké pour tester <c>HttpPlatformClient</c> sans socket réel (ni réservation
/// d'URL) : capture chaque requête (méthode, URI, en-têtes, corps) et renvoie une réponse scriptée.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

    public List<string> RequestBodies { get; } = new List<string>();

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    public static HttpResponseMessage Status(HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content != null
            ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            : string.Empty;
        Requests.Add(request);
        RequestBodies.Add(body);
        return Task.FromResult(_responder(request, body));
    }
}

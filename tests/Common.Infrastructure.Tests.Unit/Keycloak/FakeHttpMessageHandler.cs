namespace Stratum.Common.Infrastructure.Tests.Unit.Keycloak;

using System.Net;

/// <summary>
/// Fake HTTP handler that returns pre-configured responses for unit testing.
/// Tracks call count and captures request details for assertions.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<QueuedResponse> _sequentialResponses = new();
    private HttpStatusCode _defaultStatusCode = HttpStatusCode.OK;
    private string _defaultResponseBody = string.Empty;

    public int CallCount { get; private set; }

    public Uri? LastRequestUri { get; private set; }

    public HttpContent? LastRequestContent { get; private set; }

    public HttpMethod? LastRequestMethod { get; private set; }

    public string? LastAuthorizationHeader { get; private set; }

    public List<(Uri? Uri, HttpMethod Method, string? Auth)> AllRequests { get; } = [];

    /// <summary>Request bodies, one entry per call (null for body-less requests) — same index as <see cref="AllRequests"/>.</summary>
    public List<string?> AllRequestBodies { get; } = [];

    public void SetupResponse(HttpStatusCode statusCode, string body)
    {
        _defaultStatusCode = statusCode;
        _defaultResponseBody = body;
    }

    public void EnqueueResponse(HttpStatusCode statusCode, string body)
    {
        _sequentialResponses.Enqueue(new QueuedResponse(statusCode, body, null));
    }

    public void EnqueueResponseWithLocation(HttpStatusCode statusCode, string body, string locationUri)
    {
        _sequentialResponses.Enqueue(new QueuedResponse(statusCode, body, locationUri));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequestUri = request.RequestUri;
        LastRequestContent = request.Content;
        LastRequestMethod = request.Method;
        LastAuthorizationHeader = request.Headers.Authorization?.ToString();
        AllRequests.Add((request.RequestUri, request.Method, LastAuthorizationHeader));
        AllRequestBodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));

        HttpStatusCode code;
        string body;
        string? location = null;

        if (_sequentialResponses.Count > 0)
        {
            var next = _sequentialResponses.Dequeue();
            code = next.StatusCode;
            body = next.Body;
            location = next.Location;
        }
        else
        {
            code = _defaultStatusCode;
            body = _defaultResponseBody;
        }

        var response = new HttpResponseMessage(code)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        if (location is not null)
        {
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        }

        return response;
    }

    private sealed record QueuedResponse(HttpStatusCode StatusCode, string Body, string? Location);
}

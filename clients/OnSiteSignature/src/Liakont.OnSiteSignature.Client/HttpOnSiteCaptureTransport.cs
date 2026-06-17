namespace Liakont.OnSiteSignature.Client;

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

/// <summary>
/// Transport HTTPS du payload de capture vers le proxy plateforme <c>OnSiteCapture</c>
/// (<c>POST /api/v1/signature/onsite-capture</c>). Authentification derrière l'abstraction IdP : le client
/// présente un jeton porteur (obtenu de l'IdP — hors périmètre de ce capteur). Le <c>company_id</c> n'est
/// JAMAIS envoyé : le serveur le résout du principal authentifié (tenant-scoping serveur, CLAUDE.md n°9).
/// </summary>
internal sealed class HttpOnSiteCaptureTransport : IOnSiteCaptureTransport
{
    private const string CapturePath = "/api/v1/signature/onsite-capture";

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
    };

    private readonly HttpClient _httpClient;
    private readonly string _bearerToken;

    /// <summary>Crée le transport HTTPS.</summary>
    /// <param name="httpClient">Client HTTP (sa <c>BaseAddress</c> pointe sur la plateforme).</param>
    /// <param name="bearerToken">Jeton porteur de l'opérateur (IdP).</param>
    public HttpOnSiteCaptureTransport(HttpClient httpClient, string bearerToken)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _bearerToken = bearerToken ?? throw new ArgumentNullException(nameof(bearerToken));
    }

    /// <inheritdoc />
    public async Task SendAsync(OnSiteCapturePayload payload, CancellationToken cancellationToken)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        string body = JsonConvert.SerializeObject(payload, JsonSettings);
        using var request = new HttpRequestMessage(HttpMethod.Post, CapturePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

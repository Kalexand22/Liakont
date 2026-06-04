namespace Liakont.Modules.Archive.Infrastructure;

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

/// <summary>
/// Client HTTP de l'autorité d'horodatage RFC 3161 (TRK06). POST de la requête DER avec le type
/// <c>application/timestamp-query</c>, lecture de la réponse <c>application/timestamp-reply</c>. L'URL de
/// la TSA est du paramétrage d'instance (jamais versionné) ; le client nommé est configuré par
/// <c>AddArchiveModule</c>.
/// </summary>
internal sealed class HttpTsaClient : ITsaClient
{
    /// <summary>Nom du client HTTP typé (IHttpClientFactory) de la TSA.</summary>
    public const string HttpClientName = "Archive.Rfc3161Tsa";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimestampAnchorOptions _options;

    public HttpTsaClient(IHttpClientFactory httpClientFactory, IOptions<TimestampAnchorOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<byte[]> RequestTokenAsync(byte[] requestDer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestDer);

        string? tsaUrl = _options.Rfc3161.TsaUrl;
        if (string.IsNullOrWhiteSpace(tsaUrl))
        {
            throw new InvalidOperationException(
                "L'ancrage RFC 3161 requiert une URL de TSA (Archive:Anchor:Rfc3161:TsaUrl) — configuration d'instance manquante.");
        }

        using HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        using var content = new ByteArrayContent(requestDer);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");

        using HttpResponseMessage response = await client.PostAsync(tsaUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}

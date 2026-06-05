namespace Liakont.Agent.Core.Transport;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Transport;
using Newtonsoft.Json;

/// <summary>
/// Client HTTP du contrat d'ingestion (F12 §3), sur <see cref="HttpClient"/> (net48, TLS 1.2+ via la
/// configuration du <see cref="HttpClient"/> fourni). HTTPS SORTANT uniquement (F12 §2.6). Chaque
/// requête porte la clé API (<c>X-Agent-Key</c>) et la version du contrat (<c>X-Contract-Version</c>).
/// La classe ne porte AUCUNE politique (retry/backoff/re-découpe) : elle traduit la requête et le code
/// HTTP en <see cref="PlatformResponseKind"/>. Synchrone (l'agent tourne sur un thread de fond dédié).
/// </summary>
public sealed class HttpPlatformClient : IPlatformClient
{
    private const string BatchPath = "api/agent/v1/documents/batch";
    private const string StatusPath = "api/agent/v1/documents/status";
    private const string PdfPoolPath = "api/agent/v1/pdf-pool";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _contractVersion;

    /// <summary>Crée un client de plateforme.</summary>
    /// <param name="httpClient">Client HTTP configuré avec l'URL de la plateforme en <see cref="HttpClient.BaseAddress"/>.</param>
    /// <param name="apiKey">Clé API en clair (déchiffrée DPAPI à l'usage), portée par <c>X-Agent-Key</c>.</param>
    /// <param name="contractVersion">Version du contrat émise (par défaut, celle de l'assembly).</param>
    public HttpPlatformClient(HttpClient httpClient, string apiKey, string? contractVersion = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("La clé API de l'agent est requise.", nameof(apiKey));
        }

        if (_httpClient.BaseAddress is null)
        {
            throw new ArgumentException("Le client HTTP doit avoir une BaseAddress (l'URL de la plateforme).", nameof(httpClient));
        }

        _apiKey = apiKey;
        _contractVersion = string.IsNullOrWhiteSpace(contractVersion) ? AgentContractVersion.ContractVersion : contractVersion!;
    }

    /// <inheritdoc />
    public PushBatchOutcome PushDocuments(
        IReadOnlyList<string> canonicalDocumentJsons,
        IReadOnlyList<SourceTaxRegimeDto> sourceTaxRegimes)
    {
        if (canonicalDocumentJsons is null)
        {
            throw new ArgumentNullException(nameof(canonicalDocumentJsons));
        }

        string body = BuildBatchBody(_contractVersion, canonicalDocumentJsons, sourceTaxRegimes ?? Array.Empty<SourceTaxRegimeDto>());

        try
        {
            using (var request = CreateRequest(HttpMethod.Post, BatchPath))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = Send(request))
                {
                    PlatformResponseKind kind = Categorize((int)response.StatusCode);
                    if (kind != PlatformResponseKind.Ok)
                    {
                        return new PushBatchOutcome(kind, reason: ReasonFor(response));
                    }

                    string payload = ReadBody(response);
                    return new PushBatchOutcome(PlatformResponseKind.Ok, ParseResults(payload));
                }
            }
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            return new PushBatchOutcome(PlatformResponseKind.TransportError, reason: ex.Message);
        }
    }

    /// <inheritdoc />
    public PdfPushOutcome PushLinkedPdf(string sourceReference, string filePath)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            throw new ArgumentException("La référence source du PDF lié est requise.", nameof(sourceReference));
        }

        string route = $"api/agent/v1/documents/{Uri.EscapeDataString(sourceReference)}/pdf";
        return PushFile(route, filePath);
    }

    /// <inheritdoc />
    public PdfPushOutcome PushPoolPdf(string filePath) => PushFile(PdfPoolPath, filePath);

    /// <inheritdoc />
    public DocumentStatusOutcome GetDocumentStatus(string sourceReference, string payloadHash)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            throw new ArgumentException("La référence source est requise.", nameof(sourceReference));
        }

        if (string.IsNullOrWhiteSpace(payloadHash))
        {
            throw new ArgumentException("L'empreinte du payload est requise.", nameof(payloadHash));
        }

        string route = $"{StatusPath}?sourceReference={Uri.EscapeDataString(sourceReference)}&payloadHash={Uri.EscapeDataString(payloadHash)}";

        try
        {
            using (var request = CreateRequest(HttpMethod.Get, route))
            using (HttpResponseMessage response = Send(request))
            {
                int code = (int)response.StatusCode;

                // 404 = la plateforme ne connaît pas (encore) la clé : non terminal, l'agent renvoie.
                if (code == 404)
                {
                    return new DocumentStatusOutcome(PlatformResponseKind.Ok, status: null);
                }

                PlatformResponseKind kind = Categorize(code);
                if (kind != PlatformResponseKind.Ok)
                {
                    return new DocumentStatusOutcome(kind, reason: ReasonFor(response));
                }

                DocumentStatusResultDto? result = JsonConvert.DeserializeObject<DocumentStatusResultDto>(ReadBody(response));
                if (result is null)
                {
                    return new DocumentStatusOutcome(PlatformResponseKind.Ok, status: null);
                }

                return new DocumentStatusOutcome(PlatformResponseKind.Ok, result.Status, result.Reason);
            }
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            return new DocumentStatusOutcome(PlatformResponseKind.TransportError, reason: ex.Message);
        }
    }

    private static string BuildBatchBody(string contractVersion, IReadOnlyList<string> canonicalDocumentJsons, IReadOnlyList<SourceTaxRegimeDto> regimes)
    {
        var builder = new StringBuilder();
        builder.Append("{\"ContractVersion\":");
        builder.Append(JsonConvert.SerializeObject(contractVersion));
        builder.Append(",\"Documents\":[");
        for (int i = 0; i < canonicalDocumentJsons.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            // JSON canonique déjà valide : injecté tel quel pour préserver les octets hashés.
            builder.Append(canonicalDocumentJsons[i]);
        }

        builder.Append("],\"SourceTaxRegimes\":");
        builder.Append(JsonConvert.SerializeObject(regimes));
        builder.Append('}');
        return builder.ToString();
    }

    private static PlatformResponseKind Categorize(int statusCode)
    {
        if (statusCode >= 200 && statusCode <= 299)
        {
            return PlatformResponseKind.Ok;
        }

        switch (statusCode)
        {
            case 400:
                return PlatformResponseKind.BadRequest;
            case 401:
            case 403:
                return PlatformResponseKind.Unauthorized;
            case 413:
                return PlatformResponseKind.PayloadTooLarge;
            case 426:
                return PlatformResponseKind.UpgradeRequired;
            case 429:
                return PlatformResponseKind.Throttled;
            default:
                return statusCode >= 500 && statusCode <= 599
                    ? PlatformResponseKind.Throttled
                    : PlatformResponseKind.TransportError;
        }
    }

    private static IReadOnlyList<DocumentPushResultDto> ParseResults(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<DocumentPushResultDto>();
        }

        PushBatchResponseDto? response = JsonConvert.DeserializeObject<PushBatchResponseDto>(payload);
        return response?.Results ?? (IReadOnlyList<DocumentPushResultDto>)Array.Empty<DocumentPushResultDto>();
    }

    private static string ReadBody(HttpResponseMessage response) =>
        response.Content is null ? string.Empty : response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private static string ReasonFor(HttpResponseMessage response) =>
        string.Format(CultureInfo.InvariantCulture, "HTTP {0}", (int)response.StatusCode);

    private static bool IsTransport(Exception ex) =>
        ex is HttpRequestException || ex is OperationCanceledException || ex is IOException;

    private PdfPushOutcome PushFile(string route, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Le chemin du fichier PDF est requis.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            // Fichier source disparu entre l'enfilage et le push : actionnable par l'opérateur, pas
            // transitoire → catégorie terminale (l'élément sera mis en erreur et signalé, pas re-tenté).
            return new PdfPushOutcome(PlatformResponseKind.BadRequest, $"Fichier PDF introuvable : « {filePath} ».");
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            using (var request = CreateRequest(HttpMethod.Post, route))
            {
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                request.Content = content;
                using (HttpResponseMessage response = Send(request))
                {
                    PlatformResponseKind kind = Categorize((int)response.StatusCode);
                    return new PdfPushOutcome(kind, kind == PlatformResponseKind.Ok ? null : ReasonFor(response));
                }
            }
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            return new PdfPushOutcome(PlatformResponseKind.TransportError, ex.Message);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUri)
    {
        var request = new HttpRequestMessage(method, new Uri(relativeUri, UriKind.Relative));
        request.Headers.Add(AgentApiHeaders.AgentKey, _apiKey);
        request.Headers.Add(AgentApiHeaders.ContractVersion, _contractVersion);
        return request;
    }

    private HttpResponseMessage Send(HttpRequestMessage request) =>
        _httpClient.SendAsync(request).GetAwaiter().GetResult();
}

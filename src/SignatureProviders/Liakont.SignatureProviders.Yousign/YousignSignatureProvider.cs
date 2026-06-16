namespace Liakont.SignatureProviders.Yousign;

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Liakont.Modules.Signature.Contracts;
using Liakont.SignatureProviders.Yousign.Wire;

/// <summary>
/// Plug-in de signature À DISTANCE Yousign (ADR-0029 ; F17 §5). Server-side, piloté EXCLUSIVEMENT par
/// <see cref="Capabilities"/> (jamais un <c>if (provider is Yousign)</c> ailleurs — INV-YOUSIGN-1) ; ne
/// référence que <c>Signature.Contracts</c> (INV-YOUSIGN-2). Appelle l'API Yousign Public v3 sur une URL de
/// base ALLOWLISTÉE (anti-SSRF, <see cref="YousignSsrfGuardHandler"/> + <c>AllowAutoRedirect = false</c>) avec
/// la clé API Bearer du tenant (en mémoire, jamais journalisée). Le webhook est vérifié par HMAC INTERNE sur
/// le RAW body (INV-YOUSIGN-3). Le rapatriement WORM de la preuve est fait par l'APPELANT (drain) via
/// <c>Archive.Contracts</c>, jamais ici (INV-YOUSIGN-6).
/// </summary>
internal sealed class YousignSignatureProvider : ISignatureProvider
{
    private readonly HttpClient _httpClient;
    private readonly YousignAccountConfig _config;
    private readonly YousignRetryPolicy _retryPolicy;
    private readonly Func<double> _jitterSource;

    public YousignSignatureProvider(
        HttpClient httpClient,
        YousignAccountConfig config,
        YousignRetryPolicy? retryPolicy = null,
        Func<double>? jitterSource = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(config);
        _httpClient = httpClient;
        _config = config;
        _retryPolicy = retryPolicy ?? YousignRetryPolicy.Default;
        _jitterSource = jitterSource ?? Random.Shared.NextDouble;
    }

    /// <inheritdoc />
    public SignatureProviderCapabilities Capabilities => YousignCapabilities.Declared;

    /// <inheritdoc />
    public async Task<SignatureRequestResult> RequestSignatureAsync(
        SignatureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Gating PAR CAPACITÉS (jamais d'exception, jamais de blocage — INV-YOUSIGN-1). Une localisation ou un
        // niveau non déclaré (ex. QES hors offre) → résultat TYPÉ NotSupported.
        if (!Capabilities.Supports(request.RequestedMode))
        {
            return SignatureRequestResult.NotSupported(
                SignatureCapabilityNotSupportedResult.Create(Capabilities.ProviderName, request.RequestedMode));
        }

        if (!Capabilities.Supports(request.RequestedLevel))
        {
            return SignatureRequestResult.NotSupported(
                SignatureCapabilityNotSupportedResult.Create(Capabilities.ProviderName, request.RequestedLevel));
        }

        // La clé API est requise pour les appels sortants. Absente = compte inbound-only (webhook seul) : le
        // provider renvoie un résultat technique typé sans lever d'exception (jamais un 500 non contrôlé).
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            return SignatureRequestResult.Technical(
                "Clé API Yousign absente : configurez la clé API du compte de signature.");
        }

        // Crée la demande de signature côté Yousign (cycle v3 : create → … → activate → webhook, ADR-0029 §1).
        // Le payload ne porte que la corrélation (external_id = document) + le hash de binding eIDAS art. 26 d
        // quand il est fourni ; le CONTENU binaire du document est fourni par l'orchestration appelante (qui
        // détient les octets — frontière : le plug-in ne référence pas le module Documents). Le drain rapatrie
        // ensuite la preuve en WORM via Archive.Contracts.
        var payload = new
        {
            name = $"Liakont-{request.DocumentId}",
            delivery_mode = "none",
            external_id = request.DocumentId,
            document_hash = request.DocumentHash,
        };

        var outcome = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "signature_requests")
            {
                Content = JsonContent.Create(payload, options: YousignJson.Options),
            },
            cancellationToken).ConfigureAwait(false);

        if (outcome.Exception is not null || outcome.Response is null)
        {
            return SignatureRequestResult.Technical(outcome.Exception?.Message);
        }

        using var response = outcome.Response;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return SignatureRequestResult.Technical(body);
        }

        if (!response.IsSuccessStatusCode)
        {
            // 4xx (hors 429 déjà ré-essayé) = rejet d'entrée côté fournisseur ; 5xx déjà ré-essayé = technique.
            return (int)response.StatusCode >= 500
                ? SignatureRequestResult.Technical(body)
                : SignatureRequestResult.Rejected(body);
        }

        var parsed = Deserialize<YousignSignatureRequestResponse>(body);
        if (parsed?.Id is null)
        {
            return SignatureRequestResult.Technical(body);
        }

        return SignatureRequestResult.Submitted(parsed.Id, body);
    }

    /// <inheritdoc />
    public async Task<SignatureStatus> GetSignatureStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);

        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            return new SignatureStatus { ProviderReference = providerReference, State = SignatureCompletionState.Unknown };
        }

        var outcome = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"signature_requests/{Uri.EscapeDataString(providerReference)}"),
            cancellationToken).ConfigureAwait(false);

        if (outcome.Exception is not null || outcome.Response is null)
        {
            return new SignatureStatus { ProviderReference = providerReference, State = SignatureCompletionState.Unknown };
        }

        using var response = outcome.Response;
        if (!response.IsSuccessStatusCode)
        {
            return new SignatureStatus { ProviderReference = providerReference, State = SignatureCompletionState.Unknown };
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var parsed = Deserialize<YousignSignatureRequestResponse>(body);

        return new SignatureStatus
        {
            ProviderReference = providerReference,
            State = MapStatus(parsed?.Status),
            AchievedLevel = MapStatus(parsed?.Status) == SignatureCompletionState.Completed ? SignatureLevel.SES : null,
        };
    }

    /// <inheritdoc />
    public async Task<SignatureProof> DownloadProofAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);

        if (!Capabilities.SupportsProofDownload)
        {
            return SignatureProof.NotSupported(
                SignatureCapabilityNotSupportedResult.Create(Capabilities.ProviderName, SignatureCapability.ProofDownload));
        }

        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            return SignatureProof.NotSupported(
                SignatureCapabilityNotSupportedResult.Create(Capabilities.ProviderName, SignatureCapability.ProofDownload));
        }

        var outcome = await SendWithRetryAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"signature_requests/{Uri.EscapeDataString(providerReference)}/documents/download"),
            cancellationToken).ConfigureAwait(false);

        if (outcome.Exception is not null || outcome.Response is null || !outcome.Response.IsSuccessStatusCode)
        {
            outcome.Response?.Dispose();
            return SignatureProof.NotSupported(
                SignatureCapabilityNotSupportedResult.Create(Capabilities.ProviderName, SignatureCapability.ProofDownload));
        }

        using var response = outcome.Response;
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";
        return SignatureProof.Available(content, contentType);
    }

    /// <inheritdoc />
    public Task<SignatureWebhookResult> HandleWebhookAsync(
        SignatureWebhookContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Un fournisseur sans le flag Webhook renvoie NotSupported (jamais une exception — INV-SIGPROV-3).
        if (!Capabilities.CompletionTransport.HasFlag(CompletionTransport.Webhook))
        {
            return Task.FromResult(SignatureWebhookResult.NotSupported(
                SignatureCapabilityNotSupportedResult.Create(Capabilities.ProviderName, SignatureCapability.WebhookCompletion)));
        }

        // Vérification HMAC à temps constant sur les OCTETS EXACTS du corps, AVANT tout traitement
        // (INV-YOUSIGN-3). Une signature absente/malformée/falsifiée → Rejected (jamais traité).
        var rawBody = context.RawBody as byte[] ?? context.RawBody.ToArray();
        context.Headers.TryGetValue(YousignDefaults.WebhookSignatureHeader, out var signatureHeader);

        if (!YousignWebhookSignatureVerifier.IsValid(rawBody, signatureHeader, _config.WebhookSecret))
        {
            return Task.FromResult(SignatureWebhookResult.Rejected());
        }

        YousignWebhookEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<YousignWebhookEvent>(rawBody, YousignJson.Options);
        }
        catch (JsonException)
        {
            // Corps authentifié mais illisible : ignoré (pas un rejet HMAC, pas une exception remontée).
            return Task.FromResult(SignatureWebhookResult.Ignored());
        }

        var reference = evt?.Data?.SignatureRequest?.Id;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Task.FromResult(SignatureWebhookResult.Ignored());
        }

        // Seul l'événement de complétion déclenche le rapatriement WORM (ADR-0029 §5). Les autres événements
        // Yousign (ongoing, declined, expired, signer-level) sont ignorés : le HMAC était valide mais l'événement
        // n'est pas actionnable dans le périmètre SIG07 → HTTP 200 sans persistance.
        if (string.IsNullOrWhiteSpace(evt!.EventName)
            || !string.Equals(evt.EventName, YousignDefaults.CompletedEventName, StringComparison.Ordinal))
        {
            return Task.FromResult(SignatureWebhookResult.Ignored());
        }

        return Task.FromResult(SignatureWebhookResult.Accepted(reference, evt.ResolveEventId()));
    }

    private static SignatureCompletionState MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "done" => SignatureCompletionState.Completed,
        "ongoing" or "draft" or "approval" => SignatureCompletionState.Pending,
        "declined" or "rejected" => SignatureCompletionState.Declined,
        "expired" => SignatureCompletionState.Expired,
        _ => SignatureCompletionState.Unknown,
    };

    private static T? Deserialize<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, YousignJson.Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static bool IsTransientStatus(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    // Envoie une requête (re-construite à chaque tentative — un HttpRequestMessage n'est pas réutilisable) avec
    // retry + backoff/jitter sur 429 et le transitoire (réseau/5xx/timeout). 4xx (hors 429) et 401/403 ne sont
    // jamais ré-essayés (ADR-0029 §4).
    private async Task<SendOutcome> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage? response = null;
            Exception? transientError = null;
            try
            {
                using var request = requestFactory();
                response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                transientError = new TimeoutException("Délai d'attente dépassé lors de l'appel Yousign (re-tentable).");
            }
            catch (HttpRequestException ex)
            {
                transientError = ex;
            }

            var isTransient = transientError is not null
                || (response is not null && IsTransientStatus(response.StatusCode));

            if (!isTransient || attempt >= _retryPolicy.RetryCount)
            {
                return new SendOutcome(response, transientError);
            }

            response?.Dispose();
            var delay = _retryPolicy.DelayFor(attempt, _jitterSource());
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private readonly record struct SendOutcome(HttpResponseMessage? Response, Exception? Exception);
}

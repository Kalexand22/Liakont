namespace Liakont.Host.InstanceEmail;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fournisseur de jeton OAuth2 (ADR-0039) : échange <c>grant_type=refresh_token</c> contre un
/// <c>access_token</c> par un simple <c>POST application/x-www-form-urlencoded</c> sur l'endpoint du
/// fournisseur (Google / Microsoft) — AUCUN SDK. Le jeton est MIS EN CACHE et renouvelé avant expiration
/// (<c>expires_in</c> moins <see cref="TokenExpirySkew"/>). Singleton : injecte <see cref="IHttpClientFactory"/>
/// (sûr en Singleton, jamais un <c>HttpClient</c> capturé). Aucun secret (<c>client_secret</c> /
/// <c>refresh_token</c> / <c>access_token</c>) n'est journalisé (CLAUDE.md n°10/18) ; la clé de cache est un
/// hachage, pas le secret en clair. Le <c>scope</c> est omis : le rafraîchissement réutilise le consentement
/// d'origine du <c>refresh_token</c> (comportement identique Google/Microsoft). Le cache est une référence
/// atomique par clé ; sous une rare course de premiers appels concurrents chacun peut demander un jeton et
/// écraser le cache avec un jeton valide — sans conséquence (précédent <c>SuperPdpTokenProvider</c>).
/// </summary>
internal sealed partial class HttpEmailOAuthTokenProvider : IEmailOAuthTokenProvider
{
    /// <summary>Nom du client HTTP (enregistré au composition root).</summary>
    public const string HttpClientName = "EmailOAuth";

    /// <summary>Marge de sécurité : le jeton est renouvelé un peu AVANT son expiration réelle.</summary>
    private static readonly TimeSpan TokenExpirySkew = TimeSpan.FromSeconds(60);

    private static readonly Uri GoogleTokenEndpoint = new("https://oauth2.googleapis.com/token");

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpEmailOAuthTokenProvider> _logger;
    private readonly ConcurrentDictionary<string, TokenEntry> _cache = new(StringComparer.Ordinal);

    public HttpEmailOAuthTokenProvider(IHttpClientFactory httpClientFactory, ILogger<HttpEmailOAuthTokenProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(EmailOAuthTokenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = ComputeCacheKey(request);

        // Un jeton en cache encore valide est rendu sans appel réseau.
        if (_cache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow < cached.ExpiresAt)
        {
            return cached.Token;
        }

        var entry = await RequestTokenAsync(request, cancellationToken).ConfigureAwait(false);
        _cache[cacheKey] = entry;
        return entry.Token;
    }

    /// <summary>
    /// Endpoint token selon le fournisseur : Google (endpoint fixe) ou Microsoft (par tenant ; <c>common</c>
    /// à défaut). Un <see cref="EmailProviderKind.SmtpBasic"/> n'a pas d'endpoint OAuth (chemin inatteignable —
    /// le transport ne demande un jeton que pour les kinds OAuth).
    /// </summary>
    private static Uri ResolveTokenEndpoint(EmailOAuthTokenRequest request) => request.Kind switch
    {
        EmailProviderKind.GoogleOAuth2 => GoogleTokenEndpoint,
        EmailProviderKind.MicrosoftOAuth2 => new Uri(
            "https://login.microsoftonline.com/"
            + (string.IsNullOrWhiteSpace(request.TenantId) ? "common" : Uri.EscapeDataString(request.TenantId))
            + "/oauth2/v2.0/token"),
        _ => throw new InvalidOperationException(
            $"Aucun endpoint OAuth pour le mode d'authentification « {request.Kind} » (chemin inatteignable)."),
    };

    // Échéance = maintenant + expires_in - marge (renouvelé un peu avant l'expiration réelle). Une durée
    // absente/non positive force un renouvellement au prochain appel plutôt que de supposer une durée inventée.
    private static TokenEntry BuildEntry(string token, int? expiresInSeconds)
    {
        var expiresAt = expiresInSeconds is { } seconds && seconds > 0
            ? DateTimeOffset.UtcNow.AddSeconds(seconds) - TokenExpirySkew
            : DateTimeOffset.MinValue;
        return new TokenEntry(token, expiresAt);
    }

    // Clé de cache = hachage de (kind, client_id, refresh_token) : discrimine une rotation d'identifiants
    // (nouveau refresh_token → cache manqué → nouveau jeton) SANS garder un secret en clair comme clé.
    private static string ComputeCacheKey(EmailOAuthTokenRequest request)
    {
        var material = string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)request.Kind}|{request.ClientId}|{request.RefreshToken}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rafraîchissement du jeton OAuth email ({Provider}).")]
    private static partial void LogRefreshing(ILogger logger, EmailProviderKind provider);

    private async Task<TokenEntry> RequestTokenAsync(EmailOAuthTokenRequest request, CancellationToken cancellationToken)
    {
        var endpoint = ResolveTokenEndpoint(request);

        LogRefreshing(_logger, request.Kind);

        using var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("client_id", request.ClientId),
            new KeyValuePair<string, string>("client_secret", request.ClientSecret),
            new KeyValuePair<string, string>("refresh_token", request.RefreshToken),
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = form };
        var client = _httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException(
                "Délai d'attente dépassé lors du rafraîchissement du jeton OAuth email — re-tentable.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // On ne journalise NI le corps (peut contenir des détails d'identifiants) NI les secrets ;
                // seul le statut HTTP est exposé.
                throw new HttpRequestException(
                    $"Rafraîchissement du jeton OAuth email en échec (HTTP {(int)response.StatusCode}) — vérifiez client_id/secret et refresh_token.",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            var parsed = await response.Content
                .ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(parsed?.AccessToken))
            {
                throw new HttpRequestException(
                    "Réponse OAuth email sans access_token — jeton non obtenu (re-tentable).");
            }

            return BuildEntry(parsed!.AccessToken!, parsed.ExpiresIn);
        }
    }

    private sealed record OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }

        // access_token = secret : redacté du ToString() synthétisé (CLAUDE.md n°10/18, defense in depth).
        private bool PrintMembers(StringBuilder builder)
        {
            builder.Append("AccessToken = ").Append(AccessToken is null ? "null" : "***")
                .Append(", ExpiresIn = ").Append(ExpiresIn?.ToString(CultureInfo.InvariantCulture) ?? "null");
            return true;
        }
    }

    private sealed record TokenEntry(string Token, DateTimeOffset ExpiresAt)
    {
        // Token = access_token (secret) : redacté du ToString() synthétisé (CLAUDE.md n°10/18).
        private bool PrintMembers(StringBuilder builder)
        {
            builder.Append("Token = ***, ExpiresAt = ").Append(ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
            return true;
        }
    }
}

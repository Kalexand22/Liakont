namespace Liakont.PaClients.SuperPdp;

using System.Net.Http;
using System.Text.Json;
using Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Fournisseur de jeton OAuth 2.0 (client credentials) de Super PDP (F14 §3.1) : obtient un jeton via
/// <c>POST &lt;base&gt;/oauth2/token</c> (<c>grant_type=client_credentials</c> + <c>client_id</c> /
/// <c>client_secret</c> — ✅ confirmé 2026-06-11), le MET EN CACHE et le RENOUVELLE avant expiration
/// (échéance = <c>expires_in</c> moins une marge de sécurité, <see cref="SuperPdpDefaults.TokenExpirySkew"/>).
/// <para>
/// Toute la mécanique OAuth vit ICI, dans le plug-in : aucun type OAuth ne traverse <see cref="IPaClient"/>
/// (frontière F14 §7). Les secrets (<c>client_id</c> / <c>client_secret</c>) ne sont jamais journalisés
/// (CLAUDE.md n°10). Le cache est un référence atomique vers une entrée IMMUABLE (jeton + échéance) : la
/// lecture/écriture du champ est atomique en .NET, donc sans verrou ni primitive jetable (le plug-in crée
/// un client par compte à la demande — un verrou par instance fuirait). Sous une rare course de premiers
/// appels concurrents, chacun peut demander un jeton et écraser le cache avec un jeton valide — sans
/// conséquence (l'API émet un jeton frais à chaque échange).
/// </para>
/// </summary>
internal sealed class SuperPdpTokenProvider : ISuperPdpTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly Uri _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;

    private TokenEntry? _entry;

    /// <summary>Construit le fournisseur de jeton pour UN compte.</summary>
    /// <param name="httpClient">Client HTTP (TLS 1.2/1.3) utilisé pour l'appel au token-endpoint.</param>
    /// <param name="tokenEndpoint">URL absolue du token-endpoint OAuth (<c>&lt;base&gt;/oauth2/token</c>).</param>
    /// <param name="clientId">Identifiant client OAuth (en clair, transport mémoire).</param>
    /// <param name="clientSecret">Secret client OAuth (en clair, transport mémoire — jamais journalisé).</param>
    public SuperPdpTokenProvider(HttpClient httpClient, Uri tokenEndpoint, string clientId, string clientSecret)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokenEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        _httpClient = httpClient;
        _tokenEndpoint = tokenEndpoint;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Chemin rapide : un jeton en cache encore valide est rendu sans appel réseau.
        if (!forceRefresh)
        {
            var cached = _entry;
            if (cached is not null && DateTimeOffset.UtcNow < cached.ExpiresAt)
            {
                return cached.Token;
            }
        }

        return await RequestTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SuperPdpTokenResponse? ParseToken(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SuperPdpTokenResponse>(body, SuperPdpJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Échéance = maintenant + expires_in - marge de sécurité (le jeton est renouvelé un peu AVANT son
    // expiration réelle). Une durée absente/non positive force un renouvellement au prochain appel (échéance
    // minimale) plutôt que de supposer une durée inventée (CLAUDE.md n°2).
    private static TokenEntry BuildEntry(string token, int? expiresInSeconds)
    {
        var expiresAt = expiresInSeconds is { } seconds && seconds > 0
            ? DateTimeOffset.UtcNow.AddSeconds(seconds) - SuperPdpDefaults.TokenExpirySkew
            : DateTimeOffset.MinValue;
        return new TokenEntry(token, expiresAt);
    }

    // Échange client_credentials : POST application/x-www-form-urlencoded (client_secret_post). Un échec
    // (réseau, non-2xx, jeton absent) lève une HttpRequestException re-tentable : le client la classe en
    // TechnicalError (re-tentable au prochain run — F14 §3.1, même grille que l'auth B2Brouter F05 §4.1).
    // Aucun secret n'est journalisé (CLAUDE.md n°10).
    private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _clientId),
            new KeyValuePair<string, string>("client_secret", _clientSecret),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint) { Content = form };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException(
                "Délai d'attente dépassé lors de l'obtention du jeton OAuth Super PDP — re-tentable au prochain run.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Obtention du jeton OAuth Super PDP en échec (HTTP {(int)response.StatusCode}) — re-tentable après vérification des identifiants.",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            var parsed = ParseToken(body);
            if (string.IsNullOrWhiteSpace(parsed?.AccessToken))
            {
                throw new HttpRequestException(
                    "Réponse OAuth Super PDP sans access_token — jeton non obtenu (re-tentable au prochain run).");
            }

            _entry = BuildEntry(parsed!.AccessToken!, parsed.ExpiresIn);
            return parsed.AccessToken!;
        }
    }

    // Entrée de cache IMMUABLE (jeton + échéance) — un seul champ référence à échanger atomiquement, sans
    // risque de lecture partielle d'un (jeton, date) incohérent.
    private sealed record TokenEntry(string Token, DateTimeOffset ExpiresAt);
}

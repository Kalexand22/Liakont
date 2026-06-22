namespace Liakont.PaClients.ChorusPro;

using System.Net.Http;
using System.Text.Json;
using Liakont.PaClients.ChorusPro.Wire;

/// <summary>
/// Fournisseur de jeton OAuth 2.0 (client credentials) du compte PISTE (F18 §2.1) : obtient un jeton via
/// <c>POST &lt;tokenEndpoint&gt;</c> (<c>&lt;oauth&gt;/api/oauth/token</c>) en
/// <c>application/x-www-form-urlencoded</c> avec <c>grant_type=client_credentials</c> + <c>client_id</c> /
/// <c>client_secret</c> + <c>scope=openid</c> (le <c>scope=openid</c> est l'ajout PISTE au
/// <c>client_credentials</c> standard — ✅ sourcé F18 §2.1). Le jeton est MIS EN CACHE et RENOUVELÉ avant
/// expiration : l'échéance est calculée sur l'<c>expires_in</c> RÉEL renvoyé moins une marge de sécurité
/// (<see cref="ChorusProDefaults.TokenExpirySkew"/>) — jamais « 3600 s » figé (F18 §2.1 ; CLAUDE.md n°2).
/// PISTE n'émet pas de <c>refresh_token</c> : le renouvellement est un re-échange <c>client_credentials</c>.
/// <para>
/// Toute la mécanique OAuth vit ICI, dans le plug-in : aucun type OAuth ne traverse <see cref="IPaClient"/>
/// (frontière F18 §2/§7). Le provider ne fait QUE cache + renouvellement ; le retry sur <c>401</c> vit dans
/// le <see cref="ChorusProClient"/>. Les secrets (<c>client_id</c> / <c>client_secret</c>) ne sont JAMAIS
/// journalisés ni inclus dans un message d'exception (CLAUDE.md n°10). Le cache est une référence atomique
/// vers une entrée IMMUABLE (jeton + échéance) : lecture/écriture du champ atomique en .NET, sans verrou
/// (le plug-in crée un provider par compte — un verrou par instance ne servirait à rien). Sous une rare
/// course de premiers appels concurrents, chacun peut demander un jeton et écraser le cache avec un jeton
/// valide — sans conséquence (l'API émet un jeton frais à chaque échange). Modèle : <c>SuperPdpTokenProvider</c>.
/// </para>
/// </summary>
internal sealed class ChorusProTokenProvider : IChorusProTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly Uri _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _scope;

    private TokenEntry? _entry;

    /// <summary>Construit le fournisseur de jeton pour UN compte PISTE.</summary>
    /// <param name="httpClient">Client HTTP (TLS 1.2/1.3) utilisé pour l'appel au token-endpoint.</param>
    /// <param name="tokenEndpoint">URL absolue du token-endpoint OAuth2 PISTE (<c>&lt;oauth&gt;/api/oauth/token</c>).</param>
    /// <param name="clientId">Identifiant client OAuth2 PISTE (en clair, transport mémoire).</param>
    /// <param name="clientSecret">Secret client OAuth2 PISTE (en clair, transport mémoire — jamais journalisé).</param>
    /// <param name="scope">Scope OAuth2 ajouté par PISTE (<c>openid</c> — F18 §2.1).</param>
    public ChorusProTokenProvider(HttpClient httpClient, Uri tokenEndpoint, string clientId, string clientSecret, string scope)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokenEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _httpClient = httpClient;
        _tokenEndpoint = tokenEndpoint;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _scope = scope;
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

    private static ChorusProTokenResponse? ParseToken(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ChorusProTokenResponse>(body, ChorusProJson.Options);
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
            ? DateTimeOffset.UtcNow.AddSeconds(seconds) - ChorusProDefaults.TokenExpirySkew
            : DateTimeOffset.MinValue;
        return new TokenEntry(token, expiresAt);
    }

    // Échange client_credentials : POST application/x-www-form-urlencoded (client_secret_post) + scope=openid
    // (spécificité PISTE, F18 §2.1). Un échec (réseau, non-2xx, jeton absent) lève une HttpRequestException
    // re-tentable, SANS aucun secret dans le message (CLAUDE.md n°10) : le client la classera en erreur
    // technique re-tentable au prochain run.
    private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _clientId),
            new KeyValuePair<string, string>("client_secret", _clientSecret),
            new KeyValuePair<string, string>("scope", _scope),
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
                "Délai d'attente dépassé lors de l'obtention du jeton OAuth2 PISTE — re-tentable au prochain run.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Obtention du jeton OAuth2 PISTE en échec (HTTP {(int)response.StatusCode}) — re-tentable après vérification des identifiants.",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            var parsed = ParseToken(body);
            if (string.IsNullOrWhiteSpace(parsed?.AccessToken))
            {
                throw new HttpRequestException(
                    "Réponse OAuth2 PISTE sans access_token — jeton non obtenu (re-tentable au prochain run).");
            }

            _entry = BuildEntry(parsed!.AccessToken!, parsed.ExpiresIn);
            return parsed.AccessToken!;
        }
    }

    // Entrée de cache IMMUABLE (jeton + échéance) — un seul champ référence à échanger atomiquement, sans
    // risque de lecture partielle d'un (jeton, date) incohérent.
    private sealed record TokenEntry(string Token, DateTimeOffset ExpiresAt);
}

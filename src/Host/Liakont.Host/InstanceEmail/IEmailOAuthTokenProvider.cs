namespace Liakont.Host.InstanceEmail;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Obtient un <c>access_token</c> OAuth2 pour l'authentification SMTP XOAUTH2 (Gmail / Office 365, ADR-0039)
/// en échangeant le <c>refresh_token</c> stocké. Enregistré en Singleton (honore <c>expires_in</c> entre les
/// envois — un provider Scoped rafraîchirait le jeton à CHAQUE envoi → throttling de l'endpoint). Aucun SDK
/// (Google/Graph/MSAL) : un simple POST HTTP + <c>System.Text.Json</c>. Aucun secret journalisé (CLAUDE.md n°10).
/// </summary>
public interface IEmailOAuthTokenProvider
{
    /// <summary>
    /// Rend un <c>access_token</c> valide pour la requête donnée (depuis le cache si non expiré, sinon un
    /// rafraîchissement réseau). Lève <see cref="System.Net.Http.HttpRequestException"/> re-tentable en cas
    /// d'échec (réseau, non-2xx, jeton absent).
    /// </summary>
    Task<string> GetAccessTokenAsync(EmailOAuthTokenRequest request, CancellationToken cancellationToken = default);
}

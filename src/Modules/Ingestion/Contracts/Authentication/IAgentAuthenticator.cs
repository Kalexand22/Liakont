namespace Liakont.Modules.Ingestion.Contracts.Authentication;

/// <summary>
/// Authentifie une clé API d'agent présentée (en-tête <c>X-Agent-Key</c>) en résolvant le préfixe
/// vers son agent dans le REGISTRE SYSTÈME, puis en vérifiant l'empreinte de la clé (F12 §3.1).
/// La résolution est nécessairement cross-tenant (elle précède tout contexte tenant) : c'est de
/// l'infrastructure d'authentification, pas une requête métier.
/// </summary>
public interface IAgentAuthenticator
{
    /// <summary>
    /// Authentifie la clé complète présentée. Ne lève jamais sur une clé invalide : renvoie un
    /// <see cref="AgentAuthenticationResult"/> (InvalidKey / Revoked / Authenticated).
    /// </summary>
    Task<AgentAuthenticationResult> AuthenticateAsync(string? presentedKey, CancellationToken cancellationToken = default);
}

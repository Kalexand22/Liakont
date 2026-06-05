namespace Liakont.Agent.Core.Security;

/// <summary>
/// Protection des secrets de l'agent (clé API plateforme, chaîne de connexion ODBC) — F12 §2.4,
/// CLAUDE.md n°10. Les secrets ne sont JAMAIS stockés ni journalisés en clair : <c>agent.json</c>
/// ne porte que la valeur protégée, produite par <c>liakont-agent encrypt</c> (AGT05).
/// </summary>
public interface ISecretProtector
{
    /// <summary>Chiffre une valeur en clair et renvoie sa forme protégée (texte, collable dans agent.json).</summary>
    string Protect(string plaintext);

    /// <summary>Déchiffre une valeur protégée et renvoie la valeur en clair (utilisée en mémoire uniquement).</summary>
    string Unprotect(string protectedValue);
}

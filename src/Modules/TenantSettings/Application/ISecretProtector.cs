namespace Liakont.Modules.TenantSettings.Application;

/// <summary>
/// Chiffre/déchiffre un secret de tenant (clé API de PA) au repos. Abstraction de chiffrement :
/// l'implémentation par défaut s'appuie sur ASP.NET Core Data Protection (clés persistées par
/// instance — OPS01). Le clair ne transite jamais vers la base, les logs ou les réponses d'API
/// (CLAUDE.md n°10).
/// </summary>
public interface ISecretProtector
{
    /// <summary>Chiffre un secret en clair. Retourne un texte chiffré opaque, sûr à persister.</summary>
    string Protect(string plaintext);

    /// <summary>Déchiffre un texte produit par <see cref="Protect"/>. Réservé à l'usage interne du secret.</summary>
    string Unprotect(string protectedValue);
}

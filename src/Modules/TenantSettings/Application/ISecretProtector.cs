namespace Liakont.Modules.TenantSettings.Application;

/// <summary>
/// Chiffre/déchiffre un secret de tenant (clé API de PA) au repos. Abstraction de chiffrement :
/// l'implémentation par défaut s'appuie sur ASP.NET Core Data Protection (clés persistées par
/// instance — OPS01). Le clair ne transite jamais vers la base, les logs ou les réponses d'API
/// (CLAUDE.md n°10).
/// </summary>
public interface ISecretProtector
{
    /// <summary>Chiffre un secret en clair sous le purpose « clé API » (rétrocompatibilité). Texte opaque sûr à persister.</summary>
    string Protect(string plaintext);

    /// <summary>Déchiffre un texte produit par <see cref="Protect(string)"/> (purpose « clé API »). Usage interne du secret.</summary>
    string Unprotect(string protectedValue);

    /// <summary>
    /// Chiffre un secret en clair sous un <paramref name="purpose"/> explicite (isolation cryptographique
    /// par secret : un texte chiffré pour un purpose ne se déchiffre pas sous un autre). Voir
    /// <see cref="PaAccountSecretPurposes"/>.
    /// </summary>
    string Protect(string plaintext, string purpose);

    /// <summary>Déchiffre un texte produit par <see cref="Protect(string, string)"/> sous le même <paramref name="purpose"/>.</summary>
    string Unprotect(string protectedValue, string purpose);
}

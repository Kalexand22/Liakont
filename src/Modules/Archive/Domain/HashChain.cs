namespace Liakont.Modules.Archive.Domain;

/// <summary>
/// Chaîne de hashes tamper-evident du coffre, PAR TENANT (F06 ; TRK05 §3 et §5). Pour chaque entrée N
/// (paquet OU addendum) ajoutée dans l'ordre :
/// <code>chain_hash(N) = SHA256(chain_hash(N-1) + entry_hash(N))</code>
/// Toute altération a posteriori d'une pièce change son <c>entry_hash</c> et casse la chaîne à partir de
/// ce point. La genèse (première entrée) utilise un précédent vide. L'intégrité est PRODUIT : elle ne
/// dépend jamais du verrou natif du backend de stockage (blueprint §6).
/// </summary>
public static class HashChain
{
    /// <summary>Précédent conventionnel de la genèse (chaîne vide) : <c>chain_hash(1) = SHA256("" + entry_hash(1))</c>.</summary>
    public const string Genesis = "";

    /// <summary>
    /// Calcule <c>chain_hash(N)</c> à partir du <paramref name="previousChainHash"/> (<c>null</c> ou vide
    /// pour la genèse) et de l'empreinte de l'entrée courante <paramref name="entryHash"/> (hex minuscule).
    /// </summary>
    public static string Next(string? previousChainHash, string entryHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryHash);
        string previous = previousChainHash ?? Genesis;
        return Sha256Hex.OfString(previous + entryHash);
    }
}

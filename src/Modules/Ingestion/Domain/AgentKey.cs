namespace Liakont.Modules.Ingestion.Domain;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Génération, empreinte et analyse des clés d'agent (format <c>&lt;prefix&gt;.&lt;secret&gt;</c>,
/// F12 §3.1). La plateforme ne stocke JAMAIS la clé en clair : seulement le <c>prefix</c> (public,
/// indexé pour la résolution) et l'empreinte SHA-256 du secret complet (modèle <c>ApiKey</c> du
/// socle Stratum). Le secret n'est connu qu'à la génération, affiché UNE seule fois (F12 §4.2).
/// </summary>
internal static class AgentKey
{
    /// <summary>Préfixe lisible des clés d'agent (discrimine des autres clés API du socle).</summary>
    private const string PrefixMarker = "agt_";

    /// <summary>Génère une nouvelle clé d'agent (prefix aléatoire + secret 256 bits).</summary>
    public static Material Generate()
    {
        var prefix = PrefixMarker + ToCompactToken(RandomNumberGenerator.GetBytes(9));
        var secret = ToCompactToken(RandomNumberGenerator.GetBytes(32));
        var fullKey = $"{prefix}.{secret}";
        return new Material(prefix, fullKey, ComputeHash(fullKey));
    }

    /// <summary>Empreinte SHA-256 (hex minuscule, 64 caractères) d'une clé complète.</summary>
    public static string ComputeHash(string fullKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullKey));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Compare une empreinte attendue à celle d'une clé présentée, en temps constant (anti-timing).
    /// </summary>
    public static bool HashesMatch(string expectedHash, string presentedFullKey)
    {
        var presentedHash = ComputeHash(presentedFullKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedHash),
            Encoding.UTF8.GetBytes(presentedHash));
    }

    /// <summary>
    /// Extrait le préfixe d'une clé présentée (<c>prefix.secret</c>) pour la résolution en base.
    /// Renvoie <c>false</c> si la clé est nulle, vide, ou mal formée (préfixe ou secret manquant).
    /// </summary>
    public static bool TryExtractPrefix(string? presentedFullKey, out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrWhiteSpace(presentedFullKey))
        {
            return false;
        }

        var separator = presentedFullKey.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator >= presentedFullKey.Length - 1)
        {
            return false;
        }

        prefix = presentedFullKey[..separator];
        return true;
    }

    // Produit un JETON OPAQUE compact (alphanumérique) à partir d'octets aléatoires : base64 dont on
    // RETIRE les caractères non alphanumériques (+, /, =). Ce n'est PAS un encodage base64url
    // réversible — le jeton ne sert qu'à porter de l'entropie (préfixe identifiant, secret), jamais à
    // retrouver les octets. Le retrait raccourcit le jeton ; l'entropie résiduelle reste largement
    // suffisante (secret 32 octets → ~40 caractères [A-Za-z0-9]), et l'unicité du préfixe est garantie
    // par l'index unique (sinon ConflictException).
    private static string ToCompactToken(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace("+", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("=", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Matériel d'une clé fraîchement générée.</summary>
    /// <param name="Prefix">Identifiant public de la clé (stocké et indexé).</param>
    /// <param name="FullKey">Clé complète <c>prefix.secret</c> — affichée une fois, jamais persistée.</param>
    /// <param name="Hash">Empreinte SHA-256 hex (64 caractères) de la clé complète.</param>
    public readonly record struct Material(string Prefix, string FullKey, string Hash);
}

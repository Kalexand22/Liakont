namespace Liakont.SignatureProviders.Yousign;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Vérificateur HMAC-SHA256 du webhook Yousign, calculé EN INTERNE au plug-in (ADR-0029 §3 ; INV-YOUSIGN-3).
/// Décision d'architecture : le plug-in N'UTILISE PAS <c>WebhookSignature.Compute</c> de
/// <c>Stratum.Modules.Notification.Domain</c> (couche Domain d'un autre module vendored — CLAUDE.md n°6/11/14) ;
/// il calcule son HMAC avec <see cref="System.Security.Cryptography"/> seul. Le HMAC est calculé sur les
/// OCTETS EXACTS du corps reçu (jamais sur un objet reconstruit) et comparé à TEMPS CONSTANT
/// (<see cref="CryptographicOperations.FixedTimeEquals"/> sur les octets, jamais <c>string.Equals</c> sur l'hex
/// — patron <c>FleetApiKeyValidator</c>). Une signature absente/malformée/falsifiée est REJETÉE.
/// </summary>
internal static class YousignWebhookSignatureVerifier
{
    /// <summary>
    /// Vrai si l'en-tête de signature fourni correspond au HMAC-SHA256 du <paramref name="rawBody"/> sous le
    /// <paramref name="secret"/> du tenant. Comparaison à temps constant ; aucun secret ni HMAC journalisé.
    /// </summary>
    /// <param name="rawBody">Octets EXACTS du corps de la requête webhook.</param>
    /// <param name="providedSignatureHeader">Valeur de l'en-tête <c>X-Yousign-Signature-256</c> (préfixe <c>sha256=</c> toléré).</param>
    /// <param name="secret">Secret HMAC du webhook du tenant (en clair, en mémoire).</param>
    public static bool IsValid(ReadOnlySpan<byte> rawBody, string? providedSignatureHeader, string secret)
    {
        if (string.IsNullOrWhiteSpace(providedSignatureHeader) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        var providedHex = providedSignatureHeader.StartsWith(YousignDefaults.WebhookSignaturePrefix, StringComparison.OrdinalIgnoreCase)
            ? providedSignatureHeader[YousignDefaults.WebhookSignaturePrefix.Length..]
            : providedSignatureHeader;

        byte[] providedBytes;
        try
        {
            providedBytes = Convert.FromHexString(providedHex.Trim());
        }
        catch (FormatException)
        {
            // En-tête non hexadécimal → signature malformée, rejetée (jamais une exception remontée).
            return false;
        }

        Span<byte> computed = stackalloc byte[HMACSHA256.HashSizeInBytes];
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var written = HMACSHA256.HashData(keyBytes, rawBody, computed);

        // FixedTimeEquals exige des longueurs égales pour ne rien révéler par le timing : on compare les
        // octets calculés aux octets fournis (longueurs différentes → false, à temps constant côté contenu).
        return written == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(computed[..written], providedBytes);
    }
}

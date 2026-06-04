namespace Liakont.Agent.Contracts.Serialization;

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Empreinte SHA-256 du JSON canonique d'un document pivot (PIV02). Le hash sert :
/// <list type="bullet">
/// <item>à l'anti-doublon par tenant (PIV04) — un même payload re-poussé est reconnu ;</item>
/// <item>à la détection d'altération de la source (TRK03) — même référence, hash différent.</item>
/// </list>
/// Comme le JSON canonique est ASCII pur et produit par l'UNIQUE <see cref="CanonicalJsonWriter"/>
/// partagé, l'empreinte est IDENTIQUE entre l'agent (net48) et la plateforme (.NET 10) — propriété
/// vérifiée par les tests golden des deux côtés. Sortie : hexadécimal MINUSCULE de 64 caractères.
/// Utilitaire de contrat, sans logique métier.
/// </summary>
public static class PayloadHasher
{
    /// <summary>Calcule l'empreinte du JSON canonique d'un document pivot.</summary>
    /// <param name="document">Le document pivot (non nul).</param>
    /// <returns>L'empreinte SHA-256 en hexadécimal minuscule (64 caractères).</returns>
    public static string ComputeHash(PivotDocumentDto document) =>
        ComputeHash(CanonicalJson.Serialize(document));

    /// <summary>Calcule l'empreinte SHA-256 d'un JSON canonique déjà sérialisé.</summary>
    /// <param name="canonicalJson">Le JSON canonique (ASCII).</param>
    /// <returns>L'empreinte SHA-256 en hexadécimal minuscule (64 caractères).</returns>
    public static string ComputeHash(string canonicalJson)
    {
        if (canonicalJson is null)
        {
            throw new ArgumentNullException(nameof(canonicalJson));
        }

        byte[] payload = Encoding.UTF8.GetBytes(canonicalJson);
        using (var sha256 = SHA256.Create())
        {
            byte[] digest = sha256.ComputeHash(payload);
            var hex = new StringBuilder(digest.Length * 2);
            foreach (byte b in digest)
            {
                hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }
    }
}

namespace Liakont.Modules.Archive.Domain;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Empreinte SHA-256 en hexadécimal minuscule (64 caractères) — même convention que
/// <c>Liakont.Agent.Contracts.Serialization.PayloadHasher</c> (hex minuscule, <c>x2</c>), réutilisée
/// ici pour rester homogène avec le <c>payload_hash</c> du module Documents (F06 §3). C'est la brique
/// d'empreinte du coffre WORM : intégrité PRODUIT, indépendante du backend de stockage (blueprint §6).
/// </summary>
public static class Sha256Hex
{
    /// <summary>Empreinte SHA-256 (hex minuscule) d'un contenu binaire.</summary>
    public static string OfBytes(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        byte[] digest = SHA256.HashData(content);
        return ToHex(digest);
    }

    /// <summary>Empreinte SHA-256 (hex minuscule) d'une chaîne, encodée en UTF-8.</summary>
    public static string OfString(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return OfBytes(Encoding.UTF8.GetBytes(content));
    }

    private static string ToHex(byte[] digest)
    {
        var hex = new StringBuilder(digest.Length * 2);
        foreach (byte b in digest)
        {
            hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }
}

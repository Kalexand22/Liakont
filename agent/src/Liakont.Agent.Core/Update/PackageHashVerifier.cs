namespace Liakont.Agent.Core.Update;

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

/// <summary>
/// Calcule et compare l'empreinte SHA-256 d'un paquet de mise à jour (ADR-0013). Un hash seul ne fonde
/// PAS la confiance (il vient du manifeste, lui-même SIGNÉ) — il garantit que le paquet téléchargé est
/// bien celui que le manifeste authentique référence (intégrité), pas qu'il est légitime (authenticité).
/// </summary>
public static class PackageHashVerifier
{
    /// <summary>Calcule l'empreinte SHA-256 d'un fichier (hex minuscule, 64 caractères).</summary>
    /// <param name="filePath">Chemin du fichier à empreinter.</param>
    /// <returns>L'empreinte hexadécimale minuscule.</returns>
    public static string ComputeSha256Hex(string filePath)
    {
        using (var sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(filePath))
        {
            byte[] hash = sha.ComputeHash(stream);
            var builder = new System.Text.StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Vrai si l'empreinte SHA-256 du fichier égale <paramref name="expectedHex"/> (comparaison
    /// insensible à la casse). Une empreinte attendue vide/nulle ne concorde jamais (fail-closed).
    /// </summary>
    /// <param name="filePath">Chemin du paquet téléchargé.</param>
    /// <param name="expectedHex">Empreinte attendue (issue du manifeste signé).</param>
    /// <returns><c>true</c> si les empreintes concordent.</returns>
    public static bool Matches(string filePath, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex))
        {
            return false;
        }

        string actual = ComputeSha256Hex(filePath);
        return string.Equals(actual, expectedHex!.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

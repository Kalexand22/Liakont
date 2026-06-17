namespace Liakont.Modules.Signature.Infrastructure.OnSite;

using System;
using System.Security.Cryptography;

/// <summary>
/// Primitive de HASH DE BINDING PROPRE à ADR-0030 §4 (PAS ADR-0023, qui ne définit aucun hash). Calcule le
/// SHA-256 des OCTETS EXACTS de l'artefact Factur-X scellé (octet pour octet, sans re-canonicalisation) et
/// vérifie <c>re-hash == hash signé</c> à temps constant. Le MÊME flux d'octets côté client ET plateforme est
/// la condition pour que la vérification ait un sens (un client qui aurait hashé d'autres octets est rejeté).
/// </summary>
internal static class OnSiteBindingHasher
{
    /// <summary>Calcule le SHA-256 (hex minuscule) des octets exacts fournis.</summary>
    /// <param name="artifact">Octets exacts de l'artefact scellé.</param>
    public static string ComputeHex(ReadOnlySpan<byte> artifact)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(artifact, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Vérifie que l'empreinte SIGNÉE par le client correspond au re-hash des octets exacts stockés, à temps
    /// constant (<see cref="CryptographicOperations.FixedTimeEquals"/> sur les octets — jamais une comparaison
    /// de chaînes hex). Une empreinte malformée ou de longueur différente est rejetée (jamais d'exception).
    /// </summary>
    /// <param name="artifact">Octets exacts de l'artefact scellé, relus côté plateforme.</param>
    /// <param name="signedHashHex">Empreinte de binding signée par le client (hex).</param>
    public static bool Verify(ReadOnlySpan<byte> artifact, string? signedHashHex)
    {
        if (string.IsNullOrWhiteSpace(signedHashHex))
        {
            return false;
        }

        byte[] signed;
        try
        {
            signed = Convert.FromHexString(signedHashHex);
        }
        catch (FormatException)
        {
            return false;
        }

        Span<byte> computed = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(artifact, computed);

        return signed.Length == computed.Length
            && CryptographicOperations.FixedTimeEquals(signed, computed);
    }
}

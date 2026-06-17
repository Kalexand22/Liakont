namespace Liakont.OnSiteSignature.Client;

using System;
using System.Security.Cryptography;

/// <summary>
/// Primitive de HASH DE BINDING côté client (ADR-0030 §4 ; INV-ONSITE-6) : SHA-256 des OCTETS EXACTS de
/// l'artefact Factur-X scellé reçu, sans re-canonicalisation. Le client signe ce hash ; la plateforme
/// re-hashe son artefact stocké et vérifie <c>re-hash == hash signé</c>. Le MÊME flux d'octets et le MÊME
/// algorithme côté client (net48) ET plateforme (net10) sont la condition de cohérence — l'hex minuscule
/// produit ici est identique à celui de <c>OnSiteBindingHasher</c> côté plateforme.
/// </summary>
internal static class BindingHasher
{
    /// <summary>Calcule le SHA-256 (hex minuscule) des octets exacts fournis.</summary>
    /// <param name="sealedArtifact">Octets exacts de l'artefact Factur-X scellé.</param>
    /// <returns>L'empreinte SHA-256 en hexadécimal minuscule.</returns>
    public static string ComputeHex(byte[] sealedArtifact)
    {
        if (sealedArtifact is null)
        {
            throw new ArgumentNullException(nameof(sealedArtifact));
        }

        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(sealedArtifact);

        // net48 n'a pas Convert.ToHexString : encodage hex manuel (minuscule), identique au flux plateforme.
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}

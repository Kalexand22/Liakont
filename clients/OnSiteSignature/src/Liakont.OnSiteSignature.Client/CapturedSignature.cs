namespace Liakont.OnSiteSignature.Client;

using System;

/// <summary>
/// Capture BRUTE renvoyée par le pad (ADR-0030 §3) : la forme de stockage de la signature (FSS) et son
/// rendu PNG. Données de PREUVE — AUCUN gabarit / feature-vector n'en est dérivé (RGPD sobre,
/// INV-ONSITE-10). Objet immuable.
/// </summary>
internal sealed class CapturedSignature
{
    /// <summary>Crée une capture brute.</summary>
    /// <param name="signatureFormatStorage">Octets de la FSS (forme de stockage de la signature).</param>
    /// <param name="pngImage">Octets du rendu PNG de la signature manuscrite.</param>
    public CapturedSignature(byte[] signatureFormatStorage, byte[] pngImage)
    {
        SignatureFormatStorage = signatureFormatStorage ?? throw new ArgumentNullException(nameof(signatureFormatStorage));
        PngImage = pngImage ?? throw new ArgumentNullException(nameof(pngImage));
    }

    /// <summary>Octets de la forme de stockage de la signature (FSS) — preuve, jamais un gabarit.</summary>
    public byte[] SignatureFormatStorage { get; }

    /// <summary>Octets du rendu PNG de la signature manuscrite.</summary>
    public byte[] PngImage { get; }
}

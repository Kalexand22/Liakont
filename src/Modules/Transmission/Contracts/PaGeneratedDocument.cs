namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Facture électronique GÉNÉRÉE par la PA (Factur-X PDF/A-3, UBL, CII) récupérée pour l'archivage
/// (TRK05, F05 §2). Si la PA ne le permet pas, <see cref="CapabilityNotSupported"/> est renseigné et
/// le contenu est vide — JAMAIS une exception, JAMAIS un blocage du produit (acceptance PAA01).
/// </summary>
public sealed record PaGeneratedDocument
{
    /// <summary>Contenu binaire de la facture générée, ou <c>null</c> si indisponible.</summary>
    public byte[]? Content { get; init; }

    /// <summary>Format du contenu (ex. « Factur-X », « UBL », « CII »), ou <c>null</c> si indisponible.</summary>
    public string? Format { get; init; }

    /// <summary>
    /// Renseigné quand la PA ne prend pas en charge le téléchargement de la facture générée
    /// (capacité <see cref="PaCapability.DocumentRetrieval"/>) ; <c>null</c> sinon.
    /// </summary>
    public PaCapabilityNotSupportedResult? CapabilityNotSupported { get; init; }

    /// <summary>Réponse brute de la PA, conservée pour l'audit (peut être <c>null</c>).</summary>
    public string? RawResponse { get; init; }

    /// <summary>Construit un résultat porteur de la facture générée.</summary>
    /// <param name="content">Contenu binaire de la facture.</param>
    /// <param name="format">Format du contenu.</param>
    /// <param name="rawResponse">Réponse brute pour l'audit (facultatif).</param>
    public static PaGeneratedDocument Available(byte[] content, string format, string? rawResponse = null) => new()
    {
        Content = content,
        Format = format,
        RawResponse = rawResponse,
    };

    /// <summary>Construit un résultat « capacité absente » (téléchargement non supporté par la PA).</summary>
    /// <param name="capabilityGap">Détail journalisable de la capacité manquante.</param>
    public static PaGeneratedDocument NotSupported(PaCapabilityNotSupportedResult capabilityGap) => new()
    {
        CapabilityNotSupported = capabilityGap,
    };
}

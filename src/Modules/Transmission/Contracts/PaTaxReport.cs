namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Un tax report tel qu'exposé par la PA (F05 §2-§3). Lecture seule du point de vue produit. Le
/// <see cref="XmlBase64"/> n'est disponible qu'après génération du ledger DGFiP (batch ~02:00, F05 §2).
/// Conserve la réponse brute pour la piste d'audit. Les montants éventuels restent dans
/// <see cref="RawResponse"/> tant que le produit n'en a pas besoin (jamais un <c>double</c> sur un
/// montant — CLAUDE.md n°1).
/// </summary>
public sealed record PaTaxReport
{
    /// <summary>Identifiant du tax report côté PA.</summary>
    public required string Id { get; init; }

    /// <summary>Type de tax report (tel que nommé par la PA).</summary>
    public required string Type { get; init; }

    /// <summary>Transport / canal déclaré par la PA, ou <c>null</c> si absent.</summary>
    public string? Transport { get; init; }

    /// <summary>État courant du tax report.</summary>
    public required PaTaxReportState State { get; init; }

    /// <summary>XML du ledger en base64, ou <c>null</c> tant qu'il n'est pas disponible.</summary>
    public string? XmlBase64 { get; init; }

    /// <summary>Vrai si la PA signale des erreurs sur ce tax report.</summary>
    public bool HasErrors { get; init; }

    /// <summary>Réponse brute de la PA, conservée pour l'audit (peut être <c>null</c>).</summary>
    public string? RawResponse { get; init; }
}

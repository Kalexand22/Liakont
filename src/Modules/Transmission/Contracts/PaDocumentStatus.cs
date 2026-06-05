namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// État courant d'un document transmis, relu auprès de la PA (F05 §3). Conserve la réponse brute
/// pour la piste d'audit.
/// </summary>
public sealed record PaDocumentStatus
{
    /// <summary>Identifiant du document côté PA.</summary>
    public required string PaDocumentId { get; init; }

    /// <summary>État courant côté PA.</summary>
    public required PaSendState State { get; init; }

    /// <summary>Identifiants des tax reports liés, jamais <c>null</c>.</summary>
    public IReadOnlyList<string> TaxReportIds { get; init; } = [];

    /// <summary>Erreurs courantes remontées par la PA, intactes, jamais <c>null</c>.</summary>
    public IReadOnlyList<PaError> Errors { get; init; } = [];

    /// <summary>Réponse brute de la PA, conservée pour l'audit (peut être <c>null</c>).</summary>
    public string? RawResponse { get; init; }
}

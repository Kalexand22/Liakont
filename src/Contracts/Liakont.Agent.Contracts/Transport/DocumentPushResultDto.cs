namespace Liakont.Agent.Contracts.Transport;

/// <summary>
/// Résultat d'ingestion d'un document d'un lot (F12 §3.4). Réponse individuelle par document, avec
/// le motif quand il est rejeté.
/// </summary>
public sealed class DocumentPushResultDto
{
    /// <summary>Crée un résultat d'ingestion de document.</summary>
    /// <param name="sourceReference">Référence source du document concerné.</param>
    /// <param name="status">Statut d'ingestion (accepté / doublon / rejeté).</param>
    /// <param name="reason">Motif (obligatoire si rejeté ; sinon <c>null</c>).</param>
    public DocumentPushResultDto(string sourceReference, DocumentPushStatus status, string? reason = null)
    {
        SourceReference = sourceReference;
        Status = status;
        Reason = reason;
    }

    /// <summary>Référence source du document concerné.</summary>
    public string SourceReference { get; }

    /// <summary>Statut d'ingestion (accepté / doublon / rejeté).</summary>
    public DocumentPushStatus Status { get; }

    /// <summary>Motif (renseigné si rejeté).</summary>
    public string? Reason { get; }
}

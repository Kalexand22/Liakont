namespace Liakont.Agent.Contracts.Transport;

/// <summary>
/// Statut de prise en charge d'un document, renvoyé par le point de statut
/// (GET /api/agent/v1/documents/status?sourceReference=...&amp;payloadHash=... — ADR-0012).
/// Lecture seule, tenant-scopé, clé <c>(sourceReference, payloadHash)</c>. DTO pur : aucune logique.
/// La réconciliation par statut ferme la perte silencieuse d'un document reçu mais non rangé : l'agent
/// ne purge sa copie locale qu'après un état TERMINAL confirmé ici, jamais sur le seul accusé de push.
/// </summary>
public sealed class DocumentStatusResultDto
{
    /// <summary>Crée un résultat de statut de document.</summary>
    /// <param name="sourceReference">Référence source du document interrogé.</param>
    /// <param name="payloadHash">Empreinte canonique du payload interrogé.</param>
    /// <param name="status">État de prise en charge rapporté par la plateforme.</param>
    /// <param name="reason">Motif (renseigné quand le document est rejeté ; sinon <c>null</c>).</param>
    public DocumentStatusResultDto(string sourceReference, string payloadHash, DocumentIntakeStatus status, string? reason = null)
    {
        SourceReference = sourceReference;
        PayloadHash = payloadHash;
        Status = status;
        Reason = reason;
    }

    /// <summary>Référence source du document interrogé.</summary>
    public string SourceReference { get; }

    /// <summary>Empreinte canonique du payload interrogé.</summary>
    public string PayloadHash { get; }

    /// <summary>État de prise en charge rapporté par la plateforme.</summary>
    public DocumentIntakeStatus Status { get; }

    /// <summary>Motif (renseigné quand le document est rejeté).</summary>
    public string? Reason { get; }
}

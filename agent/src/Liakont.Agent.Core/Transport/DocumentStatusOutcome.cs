namespace Liakont.Agent.Core.Transport;

using Liakont.Agent.Contracts.Transport;

/// <summary>
/// Résultat d'une interrogation du point de statut (GET /api/agent/v1/documents/status — ADR-0012).
/// Quand <see cref="Kind"/> vaut <see cref="PlatformResponseKind.Ok"/> : <see cref="Status"/> porte
/// l'état rapporté, ou <c>null</c> si la plateforme ne connaît pas (encore) la clé (statut non
/// terminal — l'agent renvoie l'élément). Pour toute autre catégorie, l'agent CONSERVE l'élément
/// (rien n'est perdu) et réessaie au cycle suivant.
/// </summary>
public sealed class DocumentStatusOutcome
{
    /// <summary>Crée un résultat d'interrogation de statut.</summary>
    /// <param name="kind">Catégorie de réponse de la plateforme.</param>
    /// <param name="status">État rapporté (renseigné uniquement pour une réponse 200 « connue »).</param>
    /// <param name="reason">Détail (motif de rejet / diagnostic), si applicable.</param>
    public DocumentStatusOutcome(PlatformResponseKind kind, DocumentIntakeStatus? status = null, string? reason = null)
    {
        Kind = kind;
        Status = status;
        Reason = reason;
    }

    /// <summary>Catégorie de réponse de la plateforme.</summary>
    public PlatformResponseKind Kind { get; }

    /// <summary>État rapporté par la plateforme (<c>null</c> = clé inconnue, non terminal).</summary>
    public DocumentIntakeStatus? Status { get; }

    /// <summary>Détail (motif de rejet / diagnostic).</summary>
    public string? Reason { get; }
}

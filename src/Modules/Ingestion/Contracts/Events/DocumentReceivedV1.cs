namespace Liakont.Modules.Ingestion.Contracts.Events;

/// <summary>
/// Événement d'intégration publié (via l'outbox du socle) pour CHAQUE document accepté par l'ingestion
/// (nouveau ou altéré), F12 §3-4 / PIV04. Déclenche le pipeline aval (mapping → validation →
/// transmission) — consommé par <c>PIP01</c>. Type d'événement : <c>ingestion.document.received</c>.
/// </summary>
public sealed record DocumentReceivedV1
{
    /// <summary>Tenant propriétaire (slug) — permet au consommateur de router vers la bonne base.</summary>
    public required string TenantId { get; init; }

    /// <summary>Identifiant du document créé en état <c>Detected</c> (attribué par le module Documents).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Référence du document dans le système source.</summary>
    public required string SourceReference { get; init; }

    /// <summary>Empreinte canonique du payload (SHA-256 hex).</summary>
    public required string PayloadHash { get; init; }

    /// <summary>Horodatage de réception (UTC).</summary>
    public required DateTimeOffset ReceivedAtUtc { get; init; }
}

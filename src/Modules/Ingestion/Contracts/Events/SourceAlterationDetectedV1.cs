namespace Liakont.Modules.Ingestion.Contracts.Events;

/// <summary>
/// Événement d'intégration publié (via l'outbox) quand une référence source DÉJÀ connue est re-poussée
/// avec une empreinte de payload DIFFÉRENTE — la source a été altérée après une première réception
/// (F06 / PIV04). Consommé par <c>TRK03</c> (piste d'audit d'altération). Type d'événement :
/// <c>ingestion.source.altered</c>. Le document altéré est tout de même accepté (un
/// <see cref="DocumentReceivedV1"/> est publié en parallèle) : on signale, on ne bloque pas l'ingestion.
/// </summary>
public sealed record SourceAlterationDetectedV1
{
    /// <summary>Tenant propriétaire (slug).</summary>
    public required string TenantId { get; init; }

    /// <summary>Référence du document dans le système source.</summary>
    public required string SourceReference { get; init; }

    /// <summary>Empreinte du payload précédemment reçu pour cette référence.</summary>
    public required string PreviousPayloadHash { get; init; }

    /// <summary>Empreinte du nouveau payload (différente de la précédente).</summary>
    public required string NewPayloadHash { get; init; }

    /// <summary>Identifiant du document accepté pour le nouveau payload.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Horodatage de détection (UTC).</summary>
    public required DateTimeOffset DetectedAtUtc { get; init; }
}

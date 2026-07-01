namespace Liakont.Modules.Ged.Contracts.Events;

using System;

/// <summary>
/// Événement d'intégration publié (via l'outbox du socle) pour CHAQUE document géré accepté par l'ingestion GÉNÉRIQUE
/// (nouveau ou altéré), F19 §2.4/§4.3. DÉCLENCHEUR DURABLE de l'indexation aval : le consommateur du module GED
/// relit le pivot GED stagé, applique le mapping déclaratif (GedMapper) et écrit le <c>ManagedDocument</c> + ses
/// liens (ou range le document en <c>deferred</c>). Type d'événement : <c>ged.managed-document.received</c>.
/// <para>
/// Il vit dans <c>Ged.Contracts</c> (PAS <c>Ingestion.Contracts</c>, RL-17) : c'est un événement du canal GED,
/// disjoint du canal fiscal (<c>DocumentReceivedV1</c> déclenche le pipeline d'émission — jamais consommé par la GED,
/// jamais confondu avec celui-ci). Le module GED n'a AUCUNE contrepartie fiscale (aucun <c>documents.documents</c>
/// n'est créé par ce canal).
/// </para>
/// </summary>
public sealed record ManagedDocumentReceivedV1
{
    /// <summary>Tenant propriétaire (slug) — permet au consommateur de router vers la BONNE base tenant (index GED).</summary>
    public required string TenantId { get; init; }

    /// <summary>Identifiant du <c>ManagedDocument</c> attribué à la réception (porté à l'index, idempotence RL-04).</summary>
    public required Guid ManagedDocumentId { get; init; }

    /// <summary>Référence du document dans le système source (clé de réconciliation + relecture du staging).</summary>
    public required string SourceReference { get; init; }

    /// <summary>Empreinte canonique GED du payload (SHA-256 hex) — clé de relecture du contenu stagé.</summary>
    public required string PayloadHash { get; init; }

    /// <summary>Horodatage de réception (UTC).</summary>
    public required DateTimeOffset ReceivedAtUtc { get; init; }
}

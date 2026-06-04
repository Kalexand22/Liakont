namespace Liakont.Modules.Ingestion.Contracts;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Port de création d'un document reçu en état <c>Detected</c> sur la plateforme (F12 §3-4, PIV04).
/// L'ingestion délègue ici la création du document métier : elle ne porte AUCUNE machine à états ni
/// piste d'audit (frontière module-rules §2 — c'est le module <c>Documents</c> qui les détient).
/// </summary>
/// <remarks>
/// L'interface est définie ici à titre TRANSITOIRE (description PIV04) : le module <c>Documents</c>
/// (TRK01/TRK02) n'existe pas encore. Tant qu'il n'est pas livré, l'implémentation par défaut est un
/// no-op sûr (<c>NoOpDocumentIntake</c>) qui ne crée rien et renvoie un identifiant placeholder ; le
/// câblage réel (et le déplacement éventuel vers <c>Documents.Contracts</c>) arrive avec TRK02. Les
/// tests d'ingestion la doublent par un espion. Le déclenchement du pipeline aval (PIP01) reste, lui,
/// porté par l'événement d'intégration <see cref="Events.DocumentReceivedV1"/> publié via l'outbox.
/// </remarks>
public interface IDocumentIntake
{
    /// <summary>
    /// Crée le document accepté en état <c>Detected</c> avec l'identifiant fourni par l'ingestion.
    /// L'identifiant est attribué par l'appelant (<see cref="DetectedDocumentIntake.DocumentId"/>) afin
    /// d'être PARTAGÉ par l'entrée de réception, l'événement d'intégration et le document — une seule
    /// source de vérité, connue avant la publication de l'événement. Idempotence et anti-doublon sont
    /// assurés en amont par l'ingestion (empreinte du payload par tenant) : ce port n'est appelé que
    /// pour un document réellement nouveau, et son implémentation doit être idempotente sur l'identifiant.
    /// </summary>
    Task RegisterDetectedDocumentAsync(DetectedDocumentIntake input, CancellationToken cancellationToken = default);
}

/// <summary>Données d'un document accepté à créer en état <c>Detected</c> (entrée de <see cref="IDocumentIntake"/>).</summary>
public sealed record DetectedDocumentIntake
{
    /// <summary>Identifiant du document, attribué par l'ingestion et partagé par la réception et l'événement.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Tenant propriétaire (slug), issu de l'agent authentifié.</summary>
    public required string TenantId { get; init; }

    /// <summary>Référence du document dans le système source (réconciliation + audit).</summary>
    public required string SourceReference { get; init; }

    /// <summary>Empreinte canonique du payload (SHA-256 hex), clé d'anti-doublon et de détection d'altération.</summary>
    public required string PayloadHash { get; init; }

    /// <summary>Le document pivot reçu (porte les montants calculés par la source ; jamais transformé ici).</summary>
    public required PivotDocumentDto Document { get; init; }

    /// <summary>Horodatage de réception (UTC).</summary>
    public required DateTimeOffset ReceivedAtUtc { get; init; }
}

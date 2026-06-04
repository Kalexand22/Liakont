namespace Liakont.Modules.Documents.Domain.Entities;

using System;

/// <summary>
/// Entrée IMMUABLE de la piste d'audit d'un document (F06 §3) — cœur de la non-altération (DR6 point 2 :
/// « la suppression ou la modification d'enregistrements sans laisser de trace est un indice
/// d'irrégularité »). Le journal est APPEND-ONLY : aucun chemin d'update/delete applicatif, et la
/// garantie est renforcée AU NIVEAU BASE par des triggers (CLAUDE.md n°4 ; vérifié par test). Pour un
/// document émis, trois snapshots constituent la preuve complète (payload envoyé, réponse PA, trace de
/// mapping) — ils sont alimentés par TRK04 ; à la genèse (TRK01) seul l'événement de création est écrit.
/// </summary>
public sealed class DocumentEvent
{
    private DocumentEvent()
    {
    }

    /// <summary>Identifiant de l'entrée d'audit.</summary>
    public Guid Id { get; private set; }

    /// <summary>Document concerné.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Horodatage de l'événement (UTC).</summary>
    public DateTimeOffset TimestampUtc { get; private set; }

    /// <summary>Type d'événement (F06 §3).</summary>
    public DocumentEventType EventType { get; private set; }

    /// <summary>Détail textuel (message d'audit, lisible).</summary>
    public string? Detail { get; private set; }

    /// <summary>Snapshot du payload pivot transmis (JSON), pour un document émis (alimenté par TRK04).</summary>
    public string? PayloadSnapshot { get; private set; }

    /// <summary>Réponse brute de la Plateforme Agréée (JSON), pour une émission ou un rejet (alimenté par TRK04).</summary>
    public string? PaResponseSnapshot { get; private set; }

    /// <summary>Trace de mapping TVA appliquée (JSON, F03), pour un document émis (alimenté par TRK04).</summary>
    public string? MappingTrace { get; private set; }

    /// <summary>Identité Keycloak de l'opérateur pour une action opérateur ; <c>null</c> pour un événement système (ingestion).</summary>
    public string? OperatorIdentity { get; private set; }

    /// <summary>
    /// Crée l'événement de GENÈSE écrit à la détection du document par l'ingestion (PIV04). Événement
    /// SYSTÈME (aucun opérateur), sans snapshot (le document n'est pas encore transmis).
    /// </summary>
    public static DocumentEvent Detected(Guid documentId, DateTimeOffset occurredAtUtc)
    {
        return new DocumentEvent
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            TimestampUtc = occurredAtUtc,
            EventType = DocumentEventType.DocumentDetected,
            Detail = "Document détecté par l'ingestion (état Detected).",
            PayloadSnapshot = null,
            PaResponseSnapshot = null,
            MappingTrace = null,
            OperatorIdentity = null,
        };
    }

    /// <summary>
    /// Crée l'événement d'audit d'une TRANSITION D'ÉTAT (item TRK02), produit AUTOMATIQUEMENT par chaque
    /// transition de l'agrégat <c>Document</c> : une transition ne peut pas survenir sans son fait d'audit.
    /// Sans snapshot (les snapshots d'émission/rejet sont alimentés par TRK04). <paramref name="operatorIdentity"/>
    /// porte l'identité de l'opérateur pour une action opérateur (traitement manuel, remplacement), <c>null</c>
    /// pour une transition système (pipeline).
    /// </summary>
    public static DocumentEvent Transition(
        Guid documentId,
        DocumentEventType eventType,
        DateTimeOffset occurredAtUtc,
        string detail,
        string? operatorIdentity)
    {
        return new DocumentEvent
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            TimestampUtc = occurredAtUtc,
            EventType = eventType,
            Detail = detail,
            PayloadSnapshot = null,
            PaResponseSnapshot = null,
            MappingTrace = null,
            OperatorIdentity = operatorIdentity,
        };
    }

    /// <summary>
    /// Crée le fait d'audit d'une ALTÉRATION DE LA SOURCE DÉTECTÉE APRÈS ÉMISSION (item TRK03, F06 §3) :
    /// une <c>source_reference</c> déjà émise a été re-soumise avec une empreinte différente. Inscrit SUR
    /// le document émis (<paramref name="issuedDocumentId"/>), append-only — le document émis n'est NI
    /// réémis NI mis à jour. Événement SYSTÈME (aucun opérateur), sans snapshot. L'identifiant de l'entrée
    /// est PASSÉ par l'appelant (l'identifiant de l'événement d'intégration consommé) pour rendre la
    /// consommation IDEMPOTENTE : un rejeu de l'événement d'outbox heurte la clé primaire et n'inscrit
    /// jamais deux fois la même altération.
    /// </summary>
    public static DocumentEvent SourceAlteredAfterIssue(
        Guid id,
        Guid issuedDocumentId,
        DateTimeOffset occurredAtUtc,
        string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException("Le détail de l'altération est obligatoire (piste d'audit, F06 §3).", nameof(detail));
        }

        return new DocumentEvent
        {
            Id = id,
            DocumentId = issuedDocumentId,
            TimestampUtc = occurredAtUtc,
            EventType = DocumentEventType.DocumentSourceAlteredAfterIssue,
            Detail = detail.Trim(),
            PayloadSnapshot = null,
            PaResponseSnapshot = null,
            MappingTrace = null,
            OperatorIdentity = null,
        };
    }

    /// <summary>Reconstitue une entrée d'audit depuis la persistance (lecture).</summary>
    public static DocumentEvent Reconstitute(
        Guid id,
        Guid documentId,
        DateTimeOffset timestampUtc,
        DocumentEventType eventType,
        string? detail,
        string? payloadSnapshot,
        string? paResponseSnapshot,
        string? mappingTrace,
        string? operatorIdentity)
    {
        return new DocumentEvent
        {
            Id = id,
            DocumentId = documentId,
            TimestampUtc = timestampUtc,
            EventType = eventType,
            Detail = detail,
            PayloadSnapshot = payloadSnapshot,
            PaResponseSnapshot = paResponseSnapshot,
            MappingTrace = mappingTrace,
            OperatorIdentity = operatorIdentity,
        };
    }
}

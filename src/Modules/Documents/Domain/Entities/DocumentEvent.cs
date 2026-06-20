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
    /// Nom d'affichage de l'opérateur CAPTURÉ AU MOMENT de l'événement (item FIX305, même motif que
    /// <c>operator_name</c> du journal de mapping TVA) : la piste d'audit reste lisible même si le compte
    /// est renommé ou supprimé plus tard (jamais de résolution différée). <c>null</c> pour un événement
    /// système, ou pour un événement ANTÉRIEUR à FIX305 sans nom persisté — la restitution retombe alors
    /// sur l'<see cref="OperatorIdentity"/> (GUID), gardé comme détail technique. L'événement reste
    /// append-only : aucun nom n'est jamais réécrit après coup (CLAUDE.md n°4).
    /// </summary>
    public string? OperatorName { get; private set; }

    /// <summary>Compte de la Plateforme Agréée par lequel le document a été transmis (FX06) ; <c>null</c> hors envoi.</summary>
    public string? PaAccount { get; private set; }

    /// <summary>Identifiant du type de plug-in PA ayant transmis (FX06, p. ex. <c>generique</c>) ; <c>null</c> hors envoi.</summary>
    public string? PaPluginId { get; private set; }

    /// <summary>Horodatage UTC de l'envoi de la requête de transmission à la PA (FX06) ; <c>null</c> hors envoi.</summary>
    public DateTimeOffset? PaRequestUtc { get; private set; }

    /// <summary>Horodatage UTC de la réponse de la PA (FX06) ; <c>null</c> hors envoi.</summary>
    public DateTimeOffset? PaResponseUtc { get; private set; }

    /// <summary>Empreinte SHA-256 de l'artefact réellement transmis (le Factur-X, FX06) ; <c>null</c> hors envoi.</summary>
    public string? TransmittedArtifactHash { get; private set; }

    /// <summary>Clé d'idempotence recherchable de l'envoi à la PA (FX06) ; <c>null</c> hors envoi.</summary>
    public string? IdempotencyKey { get; private set; }

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
        string? operatorIdentity,
        string? operatorName = null)
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
            OperatorName = NormalizeOperatorName(operatorName),
        };
    }

    /// <summary>
    /// Crée l'événement d'audit d'une transition d'ÉMISSION (Issued) ou de REJET (RejectedByPa) PORTANT SES
    /// SNAPSHOTS de preuve (item TRK04, F06 §3) : pour une émission, les trois snapshots sont présents
    /// (<paramref name="payloadSnapshot"/> transmis, <paramref name="paResponseSnapshot"/> brute,
    /// <paramref name="mappingTrace"/> TVA) ; pour un rejet, <paramref name="mappingTrace"/> est <c>null</c>
    /// (le document n'a pas été émis — la trace de mapping n'est pas requise). Événement SYSTÈME (la décision
    /// PA n'est pas une action opérateur). Les snapshots sont écrits dans les colonnes jsonb append-only et
    /// ne sont jamais modifiés après coup (CLAUDE.md n°4).
    /// </summary>
    public static DocumentEvent IssuanceTransition(
        Guid documentId,
        DocumentEventType eventType,
        DateTimeOffset occurredAtUtc,
        string detail,
        string payloadSnapshot,
        string paResponseSnapshot,
        string? mappingTrace)
    {
        return new DocumentEvent
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            TimestampUtc = occurredAtUtc,
            EventType = eventType,
            Detail = detail,
            PayloadSnapshot = payloadSnapshot,
            PaResponseSnapshot = paResponseSnapshot,
            MappingTrace = mappingTrace,
            OperatorIdentity = null,
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

    /// <summary>
    /// Crée le fait d'audit d'un RAPPROCHEMENT AUTOMATIQUE d'un PDF du pool non lié (item TRK07) :
    /// correspondance de CONFIANCE HAUTE (numéro de document dans le nom de fichier ou le texte du PDF),
    /// liée sans intervention. Événement SYSTÈME (aucun opérateur), sans snapshot — la preuve (le PDF
    /// lui-même) est ajoutée au paquet d'archive en addendum chaîné (WORM, TRK05).
    /// </summary>
    public static DocumentEvent ReconciledAutomatically(Guid documentId, DateTimeOffset occurredAtUtc, string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException("Le détail du rapprochement est obligatoire (piste d'audit, TRK07).", nameof(detail));
        }

        return new DocumentEvent
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            TimestampUtc = occurredAtUtc,
            EventType = DocumentEventType.DocumentReconciledAuto,
            Detail = detail.Trim(),
            PayloadSnapshot = null,
            PaResponseSnapshot = null,
            MappingTrace = null,
            OperatorIdentity = null,
        };
    }

    /// <summary>
    /// Crée le fait d'audit d'un RAPPROCHEMENT MANUEL d'un PDF du pool non lié par un opérateur (item
    /// TRK07) : confirmation d'une proposition de confiance moyenne, ou rattachement manuel d'un
    /// orphelin. L'identité de l'opérateur est OBLIGATOIRE (un rapprochement manuel n'est jamais anonyme).
    /// Sans snapshot — la preuve (le PDF) rejoint le paquet d'archive en addendum chaîné (WORM, TRK05).
    /// </summary>
    public static DocumentEvent ReconciledManually(
        Guid documentId,
        DateTimeOffset occurredAtUtc,
        string detail,
        string operatorIdentity)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException("Le détail du rapprochement est obligatoire (piste d'audit, TRK07).", nameof(detail));
        }

        if (string.IsNullOrWhiteSpace(operatorIdentity))
        {
            throw new ArgumentException("L'identité de l'opérateur est obligatoire pour un rapprochement manuel (TRK07).", nameof(operatorIdentity));
        }

        return new DocumentEvent
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            TimestampUtc = occurredAtUtc,
            EventType = DocumentEventType.DocumentReconciledManual,
            Detail = detail.Trim(),
            PayloadSnapshot = null,
            PaResponseSnapshot = null,
            MappingTrace = null,
            OperatorIdentity = operatorIdentity.Trim(),
        };
    }

    /// <summary>
    /// Crée le fait d'audit d'un VERDICT « acheteur confirmé particulier (B2C) » du garde-fou B2B/B2C (item
    /// API02b, F08 §A.4) : l'opérateur a tranché que l'acheteur est un particulier malgré l'indice
    /// professionnel (VAL05). L'identité de l'opérateur est OBLIGATOIRE (décision jamais anonyme). Sans
    /// snapshot ; n'emporte AUCUNE transition d'état (le document reste <c>Blocked</c> jusqu'à la
    /// re-vérification — c'est elle qui débloque, le verdict ne fait que lever le garde-fou pour ce document).
    /// </summary>
    public static DocumentEvent BuyerConfirmedAsIndividual(Guid documentId, DateTimeOffset occurredAtUtc, string operatorIdentity, string? operatorName = null)
    {
        if (string.IsNullOrWhiteSpace(operatorIdentity))
        {
            throw new ArgumentException("L'identité de l'opérateur est obligatoire pour un verdict de garde-fou B2B/B2C (F08 §A.4).", nameof(operatorIdentity));
        }

        return new DocumentEvent
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            TimestampUtc = occurredAtUtc,
            EventType = DocumentEventType.DocumentBuyerConfirmedB2C,
            Detail = "Acheteur confirmé « particulier » (B2C) par l'opérateur malgré l'indice professionnel — " +
                     "garde-fou B2B/B2C levé pour ce document (F08 §A.4). Re-vérification requise pour débloquer.",
            PayloadSnapshot = null,
            PaResponseSnapshot = null,
            MappingTrace = null,
            OperatorIdentity = operatorIdentity.Trim(),
            OperatorName = NormalizeOperatorName(operatorName),
        };
    }

    /// <summary>
    /// Crée le fait d'audit d'une RE-VÉRIFICATION OPÉRATEUR ayant laissé le document BLOQUÉ (item FIX02) :
    /// l'opérateur a déclenché un recheck mais le document reste <c>Blocked</c>. L'identité de l'opérateur est
    /// OBLIGATOIRE (un recheck n'est jamais anonyme) et le <paramref name="reevaluatedReason"/> (motif agrégé
    /// frais, message opérateur) est inscrit dans le <c>Detail</c> — c'est lui que la console affiche comme
    /// motif COURANT (dernier évalué, plus de motif périmé après rechargement). Sans snapshot ; n'emporte
    /// AUCUNE transition d'état (la machine à états interdit <c>Blocked → Blocked</c>).
    /// </summary>
    public static DocumentEvent RecheckedStillBlocked(Guid documentId, DateTimeOffset occurredAtUtc, string reevaluatedReason, string operatorIdentity, string? operatorName = null)
    {
        if (string.IsNullOrWhiteSpace(reevaluatedReason))
        {
            throw new ArgumentException("Le motif réévalué est obligatoire pour tracer une re-vérification toujours bloquée (piste d'audit, F06 §3).", nameof(reevaluatedReason));
        }

        if (string.IsNullOrWhiteSpace(operatorIdentity))
        {
            throw new ArgumentException("L'identité de l'opérateur est obligatoire pour une re-vérification (piste d'audit, F06 §3).", nameof(operatorIdentity));
        }

        return new DocumentEvent
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            TimestampUtc = occurredAtUtc,
            EventType = DocumentEventType.DocumentRecheckedStillBlocked,
            Detail = reevaluatedReason.Trim(),
            PayloadSnapshot = null,
            PaResponseSnapshot = null,
            MappingTrace = null,
            OperatorIdentity = operatorIdentity.Trim(),
            OperatorName = NormalizeOperatorName(operatorName),
        };
    }

    /// <summary>
    /// Crée le fait d'audit de la JOURNALISATION DE L'ENVOI à la Plateforme Agréée (item FX06, F16 §7), posé
    /// par le pipeline à la finalisation de la transmission (FX07). Trace, en colonnes explicites et
    /// recherchables, le compte/plug-in PA, les horodatages requête/réponse, l'empreinte de l'artefact
    /// transmis (le Factur-X) et la clé d'idempotence ; la réponse PA brute est portée par
    /// <paramref name="paResponseSnapshot"/> (colonne <c>pa_response_snapshot</c> existante, jamais dupliquée).
    /// Événement SYSTÈME (la transmission n'est pas une action opérateur), append-only — jamais réécrit après
    /// coup (CLAUDE.md n°4). N'emporte AUCUNE transition d'état.
    /// </summary>
    public static DocumentEvent PaTransmissionJournaled(
        Guid documentId,
        DateTimeOffset occurredAtUtc,
        string paAccount,
        string paPluginId,
        DateTimeOffset paRequestUtc,
        DateTimeOffset paResponseUtc,
        string transmittedArtifactHash,
        string idempotencyKey,
        string paResponseSnapshot,
        string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paAccount);
        ArgumentException.ThrowIfNullOrWhiteSpace(paPluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transmittedArtifactHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(paResponseSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        return new DocumentEvent
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            TimestampUtc = occurredAtUtc,
            EventType = DocumentEventType.DocumentPaTransmissionJournaled,
            Detail = detail.Trim(),
            PayloadSnapshot = null,
            PaResponseSnapshot = paResponseSnapshot,
            MappingTrace = null,
            OperatorIdentity = null,
            PaAccount = paAccount.Trim(),
            PaPluginId = paPluginId.Trim(),
            PaRequestUtc = paRequestUtc,
            PaResponseUtc = paResponseUtc,
            TransmittedArtifactHash = transmittedArtifactHash.Trim(),
            IdempotencyKey = idempotencyKey.Trim(),
        };
    }

    /// <summary>
    /// Crée le fait d'audit de l'ENREGISTREMENT DE LA RÉFÉRENCE PA d'un dépôt ASYNCHRONE accepté (item PIPE01,
    /// D7) : une Plateforme Agréée asynchrone (p. ex. Chorus Pro) a accepté le dépôt et renvoyé un n° de flux
    /// (<paramref name="paDocumentId"/>) ; le document reste <c>Sending</c> en attendant la confirmation
    /// différée. La référence permet au raccrochage d'interroger la PA et de NE JAMAIS re-déposer le flux
    /// (anti double-dépôt, CLAUDE.md n°3). La <paramref name="paResponseSnapshot"/> (réponse brute de l'accusé
    /// de dépôt) est conservée pour la piste d'audit : c'est la SEULE preuve que la PA a accepté le dépôt avant
    /// l'émission différée. Événement SYSTÈME (le dépôt n'est pas une action opérateur), append-only — jamais
    /// réécrit après coup. N'emporte AUCUNE transition d'état.
    /// </summary>
    public static DocumentEvent PaReferenceRecorded(Guid documentId, DateTimeOffset occurredAtUtc, string paDocumentId, string? paResponseSnapshot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paDocumentId);

        return new DocumentEvent
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            TimestampUtc = occurredAtUtc,
            EventType = DocumentEventType.DocumentPaReferenceRecorded,
            Detail = $"Dépôt accepté par la Plateforme Agréée (asynchrone) sous la référence « {paDocumentId.Trim()} » — en attente de confirmation d'émission.",
            PayloadSnapshot = null,
            PaResponseSnapshot = string.IsNullOrWhiteSpace(paResponseSnapshot) ? null : paResponseSnapshot,
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
        string? operatorIdentity,
        string? operatorName = null)
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
            OperatorName = NormalizeOperatorName(operatorName),
        };
    }

    /// <summary>Normalise le nom d'affichage capturé : <c>null</c> si vide/blanc, sinon élagué (cohérent avec l'identité).</summary>
    private static string? NormalizeOperatorName(string? operatorName)
        => string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim();
}

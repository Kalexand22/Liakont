namespace Liakont.Modules.Ingestion.Domain;

using System;

/// <summary>Issue de l'évaluation anti-doublon d'un document reçu (F12 §3-4, PIV04).</summary>
public enum IngestionDecisionKind
{
    /// <summary>Document inédit pour le tenant → accepté, créé en état <c>Detected</c>.</summary>
    AcceptedNew,

    /// <summary>
    /// Référence source déjà connue, mais empreinte différente → accepté ET altération signalée
    /// (la source a changé après une première réception, F06).
    /// </summary>
    AcceptedAltered,

    /// <summary>Empreinte de payload déjà connue pour le tenant → doublon, aucun effet.</summary>
    Duplicate,
}

/// <summary>
/// Décision PURE d'ingestion d'un document, isolée de toute I/O pour être testable unitairement
/// (F12 §3-4). Confronte l'empreinte du nouveau payload à l'état connu du tenant :
/// <list type="number">
///   <item>empreinte déjà connue (tenant + hash) → <see cref="IngestionDecisionKind.Duplicate"/> ;</item>
///   <item>sinon, référence source connue avec une AUTRE empreinte → <see cref="IngestionDecisionKind.AcceptedAltered"/> ;</item>
///   <item>sinon → <see cref="IngestionDecisionKind.AcceptedNew"/>.</item>
/// </list>
/// Le doublon est évalué AVANT l'altération : un même payload re-poussé (re-push complet après
/// réinstallation d'un agent) est un doublon, jamais une altération.
/// </summary>
public readonly record struct DocumentIngestionDecision(IngestionDecisionKind Kind, string? PreviousPayloadHash)
{
    /// <summary>Le document est accepté (créé en état Detected) : nouveau ou altéré.</summary>
    public bool IsAccepted => Kind != IngestionDecisionKind.Duplicate;

    /// <summary>Le document est accepté MAIS la source a été altérée (réf. connue, empreinte différente).</summary>
    public bool IsAlteration => Kind == IngestionDecisionKind.AcceptedAltered;

    /// <summary>Évalue la décision d'ingestion d'un document.</summary>
    /// <param name="payloadAlreadyKnown">L'empreinte du payload est déjà connue pour ce tenant.</param>
    /// <param name="existingHashForSourceReference">
    /// Empreinte du dernier payload reçu pour cette référence source (tenant), ou <c>null</c> si la
    /// référence n'a jamais été reçue.
    /// </param>
    /// <param name="newPayloadHash">Empreinte canonique du nouveau payload.</param>
    public static DocumentIngestionDecision Evaluate(
        bool payloadAlreadyKnown,
        string? existingHashForSourceReference,
        string newPayloadHash)
    {
        if (string.IsNullOrEmpty(newPayloadHash))
        {
            throw new ArgumentException("L'empreinte du payload est obligatoire.", nameof(newPayloadHash));
        }

        if (payloadAlreadyKnown)
        {
            return new DocumentIngestionDecision(IngestionDecisionKind.Duplicate, null);
        }

        if (existingHashForSourceReference is not null
            && !string.Equals(existingHashForSourceReference, newPayloadHash, StringComparison.Ordinal))
        {
            return new DocumentIngestionDecision(IngestionDecisionKind.AcceptedAltered, existingHashForSourceReference);
        }

        return new DocumentIngestionDecision(IngestionDecisionKind.AcceptedNew, null);
    }
}

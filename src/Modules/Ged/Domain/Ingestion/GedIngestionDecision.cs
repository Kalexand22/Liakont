namespace Liakont.Modules.Ged.Domain.Ingestion;

using System;

/// <summary>
/// Nature d'une décision d'ingestion GED (F19 §4.3). Vocabulaire fermé, miroir Domain de la logique fiscale
/// <c>DocumentIngestionDecision</c> — mais RE-COPIÉ dans le module GED (RL-01) : le canal GED ne référence JAMAIS
/// <c>Ingestion.Domain</c> (frontière de module, F19 §6). Les deux logiques sont identiques mais indépendantes.
/// </summary>
public enum GedIngestionDecisionKind
{
    /// <summary>Empreinte inédite pour le tenant (référence source jamais reçue ou nouvelle empreinte inconnue).</summary>
    AcceptedNew,

    /// <summary>Référence source connue, empreinte différente : le document a été altéré à la source.</summary>
    AcceptedAltered,

    /// <summary>Empreinte déjà connue pour ce tenant : doublon strict (aucune réécriture, aucun événement).</summary>
    Duplicate,
}

/// <summary>
/// Décision PURE d'ingestion d'un document géré (F19 §4.3, item GED05b). RE-COPIE EXACTE de la logique fiscale
/// <c>DocumentIngestionDecision.Evaluate</c> dans le module GED (RL-01) — PAS une référence à <c>Ingestion.Domain</c>
/// (frontière de module, F19 §6). Trois cas fermés : <see cref="GedIngestionDecisionKind.Duplicate"/> (empreinte déjà
/// connue) / <see cref="GedIngestionDecisionKind.AcceptedAltered"/> (référence connue, empreinte différente) /
/// <see cref="GedIngestionDecisionKind.AcceptedNew"/> (inédite). L'ORDRE D'ÉVALUATION est significatif : le doublon
/// strict est testé AVANT l'altération, de sorte qu'un renvoi du même contenu (agent réinstallé) reste un doublon,
/// jamais une fausse altération.
/// </summary>
/// <param name="Kind">La nature de la décision.</param>
/// <param name="PreviousPayloadHash">L'empreinte précédente de la référence source (renseignée seulement si altération).</param>
public readonly record struct GedIngestionDecision(GedIngestionDecisionKind Kind, string? PreviousPayloadHash)
{
    /// <summary>Vrai si le document est accepté (nouveau ou altéré) — un doublon ne l'est pas.</summary>
    public bool IsAccepted => Kind != GedIngestionDecisionKind.Duplicate;

    /// <summary>Vrai si le document est une altération d'une réception antérieure de la même référence source.</summary>
    public bool IsAlteration => Kind == GedIngestionDecisionKind.AcceptedAltered;

    /// <summary>
    /// Évalue la décision d'ingestion à partir de l'état d'anti-doublon du tenant.
    /// </summary>
    /// <param name="payloadAlreadyKnown">Vrai si l'empreinte (tenant + payload_hash) est déjà connue.</param>
    /// <param name="existingHashForSourceReference">Dernière empreinte reçue pour la référence source, ou <see langword="null"/>.</param>
    /// <param name="newPayloadHash">Empreinte du document courant (non nulle).</param>
    /// <returns>La décision d'ingestion.</returns>
    public static GedIngestionDecision Evaluate(
        bool payloadAlreadyKnown,
        string? existingHashForSourceReference,
        string newPayloadHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPayloadHash);

        // Doublon strict TESTÉ EN PREMIER : un renvoi du même contenu est un doublon, jamais une altération.
        if (payloadAlreadyKnown)
        {
            return new GedIngestionDecision(GedIngestionDecisionKind.Duplicate, null);
        }

        // Référence source connue mais empreinte DIFFÉRENTE : altération (le document a changé à la source).
        if (existingHashForSourceReference is not null
            && !string.Equals(existingHashForSourceReference, newPayloadHash, StringComparison.Ordinal))
        {
            return new GedIngestionDecision(GedIngestionDecisionKind.AcceptedAltered, existingHashForSourceReference);
        }

        // Inédite pour le tenant.
        return new GedIngestionDecision(GedIngestionDecisionKind.AcceptedNew, null);
    }
}

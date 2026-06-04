namespace Liakont.Modules.Reconciliation.Domain;

using System;

/// <summary>
/// Décision du moteur de rapprochement (item TRK07) pour un PDF du pool : le verdict, le document
/// rapproché le cas échéant, la stratégie et la confiance, et un motif lisible (français, audit). Objet
/// de valeur immuable produit par <see cref="ReconciliationEngine"/> ; les effets (addendum d'archive,
/// fait d'audit, file d'attente) sont appliqués par la couche Application.
/// </summary>
public sealed class ReconciliationDecision
{
    private ReconciliationDecision(
        ReconciliationOutcome outcome,
        Guid? matchedDocumentId,
        MatchStrategy? strategy,
        MatchConfidence? confidence,
        string reason)
    {
        Outcome = outcome;
        MatchedDocumentId = matchedDocumentId;
        Strategy = strategy;
        Confidence = confidence;
        Reason = reason;
    }

    public ReconciliationOutcome Outcome { get; }

    /// <summary>Document rapproché (renseigné pour <see cref="ReconciliationOutcome.AutoLinked"/> et <see cref="ReconciliationOutcome.ProposeManual"/>).</summary>
    public Guid? MatchedDocumentId { get; }

    public MatchStrategy? Strategy { get; }

    public MatchConfidence? Confidence { get; }

    /// <summary>Motif lisible (français) — explique le verdict pour la file d'attente et la piste d'audit.</summary>
    public string Reason { get; }

    /// <summary>Lien automatique (confiance haute, candidat unique).</summary>
    public static ReconciliationDecision AutoLinked(Guid documentId, MatchStrategy strategy, string reason) =>
        new(ReconciliationOutcome.AutoLinked, documentId, strategy, MatchConfidence.High, reason);

    /// <summary>Proposition de confiance moyenne (candidat unique) à confirmer par un opérateur.</summary>
    public static ReconciliationDecision ProposeManual(Guid documentId, string reason) =>
        new(ReconciliationOutcome.ProposeManual, documentId, MatchStrategy.DateAndAmount, MatchConfidence.Medium, reason);

    /// <summary>Non réconcilié : aucune correspondance ou ambiguïté (≥ 2 candidats).</summary>
    public static ReconciliationDecision NotReconciled(string reason) =>
        new(ReconciliationOutcome.NotReconciled, null, null, null, reason);
}

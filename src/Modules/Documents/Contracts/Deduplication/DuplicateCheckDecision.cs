namespace Liakont.Modules.Documents.Contracts.Deduplication;

/// <summary>
/// Verdict d'anti-doublon F06 §4 exposé au pipeline (item TRK03). Quatre issues, calquées EXACTEMENT sur
/// les quatre règles de F06 §4 (aucune règle inventée — CLAUDE.md n°2).
/// </summary>
public enum DuplicateCheckDecision
{
    /// <summary>F06 §4.5 — document inédit : envoi autorisé.</summary>
    Send,

    /// <summary>
    /// F06 §4.3 — un document de même clé fonctionnelle est <c>RejectedByPa</c> : renvoi autorisé (la
    /// SOURCE crée le nouveau numéro), l'ancien rejeté à passer <c>Superseded</c> (action opérateur TRK02).
    /// </summary>
    ResendSupersedingRejected,

    /// <summary>F06 §4.2 — un document de même clé fonctionnelle est déjà <c>Issued</c> : doublon, ne pas renvoyer.</summary>
    BlockedAlreadyIssued,

    /// <summary>F06 §4.4 — empreinte de payload identique à un document déjà <c>Issued</c> : doublon STRICT, bloqué.</summary>
    BlockedStrictDuplicate,
}

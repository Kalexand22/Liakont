namespace Liakont.Modules.Documents.Domain.Deduplication;

/// <summary>
/// Verdict de l'anti-doublon F06 §4, appliqué AVANT tout envoi d'un document (item TRK03). Les quatre
/// issues correspondent EXACTEMENT aux quatre règles de F06 §4 (aucune règle inventée — CLAUDE.md n°2) :
/// </summary>
public enum DocumentDuplicateOutcome
{
    /// <summary>
    /// F06 §4.5 — aucun antécédent bloquant : l'envoi est autorisé (document inédit).
    /// </summary>
    Send,

    /// <summary>
    /// F06 §4.3 — un document de même <c>(supplier_siren, document_number)</c> est en état
    /// <c>RejectedByPa</c> : le renvoi est autorisé (sous un nouveau numéro créé par la SOURCE), et
    /// l'ancien document rejeté doit passer <c>Superseded</c> (action opérateur, TRK02). Le verdict
    /// AUTORISE l'envoi et expose la référence du document rejeté à superséder.
    /// </summary>
    ResendSupersedingRejected,

    /// <summary>
    /// F06 §4.2 — un document de même <c>(supplier_siren, document_number)</c> est déjà <c>Issued</c> :
    /// doublon, NE PAS renvoyer (bloqué).
    /// </summary>
    BlockedAlreadyIssued,

    /// <summary>
    /// F06 §4.4 — un document de même empreinte de payload (<c>payload_hash</c>) est déjà <c>Issued</c> :
    /// doublon STRICT (ré-extraction involontaire d'un contenu déjà émis), bloqué.
    /// </summary>
    BlockedStrictDuplicate,
}

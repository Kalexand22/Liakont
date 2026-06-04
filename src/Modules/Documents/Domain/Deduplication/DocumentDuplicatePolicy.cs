namespace Liakont.Modules.Documents.Domain.Deduplication;

using System.Collections.Generic;
using Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Anti-doublon AVANT envoi (item TRK03) — implémentation EXACTE des quatre règles de F06 §4, dans
/// l'ordre de la spec. Aucune règle inventée (CLAUDE.md n°2) : la logique transcrit verbatim la séquence
/// « chercher par (supplier_siren, document_number) ; Issued → bloquer ; RejectedByPa → renvoi autorisé,
/// ancien superseded ; empreinte identique déjà Issued → doublon strict bloqué ; sinon → envoi ».
/// Fonction PURE : aucune dépendance d'I/O — les antécédents sont fournis par le lecteur (repository).
/// </summary>
public static class DocumentDuplicatePolicy
{
    /// <summary>
    /// Décide si un document candidat peut être envoyé, à partir de ses antécédents en base.
    /// </summary>
    /// <param name="priorsBySupplierAndNumber">
    /// Documents existants de même <c>(supplier_siren, document_number)</c> que le candidat, le candidat
    /// EXCLU. Pour un verdict déterministe quand plusieurs antécédents partagent un état, l'appelant les
    /// fournit triés par mise à jour décroissante (le plus récent l'emporte).
    /// </param>
    /// <param name="issuedDocumentIdWithSamePayloadHash">
    /// Identifiant d'un document <c>Issued</c> (toute clé fonctionnelle) de même <c>payload_hash</c> que
    /// le candidat, le candidat EXCLU, ou <c>null</c> s'il n'en existe pas (règle 4.4).
    /// </param>
    public static DocumentDuplicateDecision Decide(
        IReadOnlyCollection<PriorDocumentMatch> priorsBySupplierAndNumber,
        Guid? issuedDocumentIdWithSamePayloadHash)
    {
        ArgumentNullException.ThrowIfNull(priorsBySupplierAndNumber);

        // F06 §4.2 — un antécédent de même clé fonctionnelle déjà émis : doublon, ne pas renvoyer.
        foreach (var prior in priorsBySupplierAndNumber)
        {
            if (prior.State == DocumentState.Issued)
            {
                return new DocumentDuplicateDecision(DocumentDuplicateOutcome.BlockedAlreadyIssued, prior.Id);
            }
        }

        // F06 §4.3 — un antécédent de même clé fonctionnelle rejeté : renvoi autorisé, l'ancien à superséder.
        foreach (var prior in priorsBySupplierAndNumber)
        {
            if (prior.State == DocumentState.RejectedByPa)
            {
                return new DocumentDuplicateDecision(DocumentDuplicateOutcome.ResendSupersedingRejected, prior.Id);
            }
        }

        // F06 §4.4 — empreinte de payload identique à un document déjà émis : doublon STRICT, bloqué.
        if (issuedDocumentIdWithSamePayloadHash is { } strictDuplicateId)
        {
            return new DocumentDuplicateDecision(DocumentDuplicateOutcome.BlockedStrictDuplicate, strictDuplicateId);
        }

        // F06 §4.5 — aucun antécédent bloquant : envoi autorisé.
        return new DocumentDuplicateDecision(DocumentDuplicateOutcome.Send, RelatedDocumentId: null);
    }
}

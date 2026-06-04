namespace Liakont.Modules.Documents.Contracts.Reconciliation;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Port exposé par le module Documents au module Reconciliation (item TRK07) pour INSCRIRE sur
/// un document émis le fait d'audit d'un rapprochement PDF. La piste d'audit (<c>DocumentEvent</c>) est
/// interne au module Documents (append-only, garantie par triggers base — CLAUDE.md n°4) : un autre
/// module ne peut pas écrire un <c>DocumentEvent</c> directement (frontière Contracts-only, CLAUDE.md
/// n°14). Ce port est la SEULE surface autorisée. L'écriture est TENANT-SCOPÉE par construction (la
/// connexion EST la base du tenant courant — blueprint §7).
/// </summary>
public interface IDocumentReconciliationJournal
{
    /// <summary>
    /// Inscrit un événement <c>DocumentReconciledAuto</c> (rapprochement automatique de confiance haute)
    /// sur le document émis. Événement SYSTÈME (aucun opérateur). Lève si le document est inconnu.
    /// </summary>
    Task RecordAutomaticReconciliationAsync(Guid documentId, string detail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inscrit un événement <c>DocumentReconciledManual</c> (rapprochement confirmé/rattaché par un
    /// opérateur) sur le document émis. <paramref name="operatorIdentity"/> est obligatoire. Lève si le
    /// document est inconnu.
    /// </summary>
    Task RecordManualReconciliationAsync(Guid documentId, string detail, string operatorIdentity, CancellationToken cancellationToken = default);
}

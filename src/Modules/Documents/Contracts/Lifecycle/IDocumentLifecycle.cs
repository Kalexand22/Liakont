namespace Liakont.Modules.Documents.Contracts.Lifecycle;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Port de transition d'état d'un document (frontière Contracts-only, module-rules §3) : la SEULE surface
/// par laquelle le pipeline (PIP01c, SEND) fait avancer un document dans la machine à états du module
/// Documents (TRK02). Chaque transition est TENANT-SCOPÉE (la connexion EST le tenant) et ATOMIQUE (état
/// + événement d'audit append-only dans la même transaction). Lève si le document est inconnu, ou si la
/// transition est illégale (machine à états). Une autre module ne mute jamais un Document directement.
/// </summary>
public interface IDocumentLifecycle
{
    /// <summary>→ Blocked avec le(s) motif(s) de blocage (persisté(s) dans la piste d'audit append-only).</summary>
    Task BlockAsync(Guid documentId, string reason, CancellationToken cancellationToken = default);

    /// <summary>→ ReadyToSend en consignant la version de table de mapping TVA appliquée (obligatoire — F03).</summary>
    Task MarkReadyToSendAsync(Guid documentId, string mappingVersion, CancellationToken cancellationToken = default);

    /// <summary>ReadyToSend → Sending : la transmission à la Plateforme Agréée est engagée.</summary>
    Task BeginSendingAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Sending → Issued avec les snapshots de preuve OBLIGATOIRES (payload, réponse PA, trace mapping).</summary>
    Task MarkIssuedAsync(Guid documentId, DocumentIssuanceSnapshots snapshots, CancellationToken cancellationToken = default);

    /// <summary>Sending → RejectedByPa avec les snapshots de la tentative (payload + réponse de rejet brute).</summary>
    Task MarkRejectedByPaAsync(Guid documentId, DocumentRejectionSnapshots snapshots, CancellationToken cancellationToken = default);

    /// <summary>Sending → TechnicalError : erreur technique de transmission, re-tentable.</summary>
    Task MarkTechnicalErrorAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Action OPÉRATEUR (API02c, console) : <c>Blocked</c> ou <c>RejectedByPa</c> → <c>ManuallyHandled</c>
    /// (état terminal) — le document est traité hors passerelle. Le <paramref name="reason"/> (motif, F06 §3)
    /// et l'<paramref name="operatorIdentity"/> sont OBLIGATOIRES et inscrits dans la piste d'audit append-only.
    /// Retourne un RÉSULTAT (pas d'exception, car le refus est attendu) : <see cref="DocumentResolutionOutcome.DocumentNotFound"/>
    /// si le document est inconnu dans le tenant, <see cref="DocumentResolutionOutcome.InvalidState"/> si l'état
    /// courant n'autorise pas la transition.
    /// </summary>
    Task<DocumentResolutionOutcome> ResolveManuallyAsync(
        Guid documentId, string reason, string operatorIdentity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Action OPÉRATEUR (API02c, console) : <c>RejectedByPa</c> → <c>Superseded</c> (état terminal) — lie le
    /// document rejeté à son REMPLAÇANT (<paramref name="replacementDocumentId"/>), déjà reçu dans le tenant via
    /// l'agent (le logiciel source est le seul créateur de numéros, F06 §4 : la passerelle n'invente jamais de
    /// référence de remplacement). L'<paramref name="operatorIdentity"/> est OBLIGATOIRE (audit). Retourne un
    /// RÉSULTAT : <see cref="DocumentResolutionOutcome.DocumentNotFound"/> (document rejeté inconnu),
    /// <see cref="DocumentResolutionOutcome.InvalidState"/> (le document n'est pas <c>RejectedByPa</c>),
    /// <see cref="DocumentResolutionOutcome.ReplacementNotFound"/> (remplaçant absent du tenant).
    /// </summary>
    Task<DocumentResolutionOutcome> SupersedeAsync(
        Guid documentId, Guid replacementDocumentId, string operatorIdentity, CancellationToken cancellationToken = default);
}

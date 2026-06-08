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
    /// Verdict garde-fou B2B/B2C (item API02b, F08 §A.4) : depuis <c>Blocked</c>, enregistre que l'opérateur
    /// a confirmé l'acheteur « particulier » (B2C) malgré l'indice professionnel (VAL05). NE CHANGE PAS l'état
    /// (la re-vérification débloque ensuite) ; pose le marqueur persistant + un fait d'audit append-only portant
    /// l'identité de l'opérateur (OBLIGATOIRE). Lève si le document est inconnu ou n'est pas <c>Blocked</c>.
    /// </summary>
    Task ConfirmBuyerAsIndividualAsync(Guid documentId, string operatorIdentity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Action OPÉRATEUR « traité manuellement hors passerelle » (item API02b/API02c, TRK02) : depuis
    /// <c>Blocked</c> ou <c>RejectedByPa</c> → état terminal <c>ManuallyHandled</c>. <paramref name="reason"/>
    /// (motif journalisé) et <paramref name="operatorIdentity"/> sont OBLIGATOIRES (piste d'audit, F06 §3).
    /// Lève si le document est inconnu ou si la transition est illégale (machine à états).
    /// </summary>
    Task MarkManuallyHandledAsync(Guid documentId, string reason, string operatorIdentity, CancellationToken cancellationToken = default);
}

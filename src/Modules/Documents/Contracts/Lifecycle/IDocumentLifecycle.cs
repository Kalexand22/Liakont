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

    /// <summary>
    /// Action OPÉRATEUR (item FIX02, re-vérification console) : <c>Blocked</c> → <c>ReadyToSend</c> déclenché par
    /// un recheck d'opérateur. Identique à <see cref="MarkReadyToSendAsync"/> mais le fait d'audit append-only
    /// porte l'<paramref name="operatorIdentity"/> (OBLIGATOIRE, GUID) et l'<paramref name="operatorName"/>
    /// (nom d'affichage capturé au moment du geste, item FIX305 — peut être <c>null</c>, repli sur le GUID) :
    /// la re-vérification réussie n'est pas un déblocage système anonyme mais un geste opérateur tracé
    /// (auteur + résultat, F06 §3). Le déblocage est
    /// vérifié SOUS le verrou <c>FOR UPDATE</c> et un changement d'état concurrent est retourné comme
    /// <see cref="DocumentRecheckPersistOutcome.StateChanged"/> (jamais une exception → pas de 500, pas de TOCTOU).
    /// </summary>
    Task<DocumentRecheckPersistOutcome> MarkReadyToSendByRecheckAsync(Guid documentId, string mappingVersion, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Action OPÉRATEUR (item FIX02, re-vérification console) : trace une re-vérification ayant laissé le document
    /// <c>Blocked</c>. Inscrit un fait d'audit append-only (auteur OBLIGATOIRE — <paramref name="operatorIdentity"/>
    /// GUID + <paramref name="operatorName"/> nom affiché capturé, FIX305 — et motif RÉÉVALUÉ) SANS changer
    /// l'état (la machine à états interdit <c>Blocked → Blocked</c>). Rend l'action opérateur visible dans la piste
    /// (F06 §3) et fait du motif inscrit le motif COURANT affiché (dernier évalué — plus de motif périmé après
    /// rechargement). L'état est vérifié SOUS le verrou <c>FOR UPDATE</c> : si un geste concurrent a sorti le
    /// document de <c>Blocked</c>, rien n'est inscrit et <see cref="DocumentRecheckPersistOutcome.StateChanged"/>
    /// est retourné (jamais une exception → pas de 500, pas de faux audit, pas de TOCTOU).
    /// </summary>
    Task<DocumentRecheckPersistOutcome> RecordRecheckStillBlockedAsync(Guid documentId, string reevaluatedReason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Action OPÉRATEUR (re-vérification console) : <c>RejectedByPa</c> → <c>Blocked</c> déclenché par un recheck
    /// d'opérateur sur un document rejeté par la Plateforme Agréée dont la cause N'EST PAS corrigée. Le document
    /// quitte l'état rejeté (cul-de-sac) pour <c>Blocked</c>, qui affiche le motif réévalué à corriger (« bloquer
    /// plutôt qu'envoyer faux », CLAUDE.md n°3). Inscrit la transition + un fait d'audit append-only portant
    /// l'identité de l'opérateur (OBLIGATOIRE — <paramref name="operatorIdentity"/> GUID + <paramref name="operatorName"/>
    /// nom affiché capturé, FIX305) et le motif RÉÉVALUÉ (<paramref name="reevaluatedReason"/>), qui devient le
    /// motif COURANT affiché. La légalité est vérifiée SOUS le verrou <c>FOR UPDATE</c> : si un geste concurrent a
    /// sorti le document de <c>RejectedByPa</c>, rien n'est inscrit et
    /// <see cref="DocumentRecheckPersistOutcome.StateChanged"/> est retourné (jamais une exception → pas de 500,
    /// pas de faux audit, pas de TOCTOU).
    /// </summary>
    Task<DocumentRecheckPersistOutcome> MarkBlockedByRecheckAsync(Guid documentId, string reevaluatedReason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default);

    /// <summary>ReadyToSend → Sending : la transmission à la Plateforme Agréée est engagée.</summary>
    Task BeginSendingAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre la RÉFÉRENCE PA (n° de flux) d'un dépôt ASYNCHRONE accepté, document RESTÉ <c>Sending</c>
    /// (item PIPE01, D7) — PAS de transition d'état. Une Plateforme Agréée asynchrone (p. ex. Chorus Pro) a
    /// accepté le dépôt sans encore l'émettre : la <paramref name="paDocumentId"/> est persistée pour que le
    /// raccrochage (RecoverSendingAsync) interroge la PA par cette référence et NE re-dépose JAMAIS le flux
    /// (anti double-dépôt async, CLAUDE.md n°3). La <paramref name="paResponseSnapshot"/> (réponse brute de
    /// l'accusé de dépôt, ou <c>null</c>) est conservée dans la piste d'audit comme preuve de l'acceptation
    /// asynchrone. Tenant-scopé et ATOMIQUE (référence + fait d'audit append-only dans la même transaction).
    /// Lève si le document est inconnu ou n'est pas <c>Sending</c>.
    /// </summary>
    Task RecordPaSendingReferenceAsync(Guid documentId, string paDocumentId, string? paResponseSnapshot, CancellationToken cancellationToken = default);

    /// <summary>Sending → Issued avec les snapshots de preuve OBLIGATOIRES (payload, réponse PA, trace mapping).</summary>
    Task MarkIssuedAsync(Guid documentId, DocumentIssuanceSnapshots snapshots, CancellationToken cancellationToken = default);

    /// <summary>Sending → RejectedByPa avec les snapshots de la tentative (payload + réponse de rejet brute).</summary>
    Task MarkRejectedByPaAsync(Guid documentId, DocumentRejectionSnapshots snapshots, CancellationToken cancellationToken = default);

    /// <summary>Sending → TechnicalError : erreur technique de transmission, re-tentable.</summary>
    Task MarkTechnicalErrorAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Action OPÉRATEUR (API02c, console) : <c>Blocked</c> ou <c>RejectedByPa</c> → <c>ManuallyHandled</c>
    /// (état terminal) — le document est traité hors passerelle. Le <paramref name="reason"/> (motif, F06 §3)
    /// et l'<paramref name="operatorIdentity"/> (GUID) sont OBLIGATOIRES et inscrits dans la piste d'audit
    /// append-only ; l'<paramref name="operatorName"/> (nom affiché capturé, FIX305) l'accompagne (peut être <c>null</c>).
    /// Retourne un RÉSULTAT (pas d'exception, car le refus est attendu) : <see cref="DocumentResolutionOutcome.DocumentNotFound"/>
    /// si le document est inconnu dans le tenant, <see cref="DocumentResolutionOutcome.InvalidState"/> si l'état
    /// courant n'autorise pas la transition. Réutilisé par le verdict garde-fou « traiter manuellement » (API02b).
    /// </summary>
    Task<DocumentResolutionOutcome> ResolveManuallyAsync(
        Guid documentId, string reason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Action OPÉRATEUR (API02c, console) : <c>RejectedByPa</c> → <c>Superseded</c> (état terminal) — lie le
    /// document rejeté à son REMPLAÇANT (<paramref name="replacementDocumentId"/>), déjà reçu dans le tenant via
    /// l'agent (le logiciel source est le seul créateur de numéros, F06 §4 : la passerelle n'invente jamais de
    /// référence de remplacement). L'<paramref name="operatorIdentity"/> (GUID) est OBLIGATOIRE (audit) ;
    /// l'<paramref name="operatorName"/> (nom affiché capturé, FIX305) l'accompagne (peut être <c>null</c>). Retourne un
    /// RÉSULTAT : <see cref="DocumentResolutionOutcome.DocumentNotFound"/> (document rejeté inconnu),
    /// <see cref="DocumentResolutionOutcome.InvalidState"/> (le document n'est pas <c>RejectedByPa</c>),
    /// <see cref="DocumentResolutionOutcome.ReplacementNotFound"/> (remplaçant absent du tenant).
    /// </summary>
    Task<DocumentResolutionOutcome> SupersedeAsync(
        Guid documentId, Guid replacementDocumentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verdict garde-fou B2B/B2C (item API02b, F08 §A.4) : depuis <c>Blocked</c>, enregistre que l'opérateur
    /// a confirmé l'acheteur « particulier » (B2C) malgré l'indice professionnel (VAL05). NE CHANGE PAS l'état
    /// (la re-vérification débloque ensuite) ; pose le marqueur persistant + un fait d'audit append-only portant
    /// l'identité de l'opérateur (<paramref name="operatorIdentity"/> GUID OBLIGATOIRE + <paramref name="operatorName"/>
    /// nom affiché capturé, FIX305). Lève si le document est inconnu ou n'est pas <c>Blocked</c>.
    /// (L'autre branche du verdict — « traiter manuellement » — réutilise <see cref="ResolveManuallyAsync"/>.)
    /// </summary>
    Task ConfirmBuyerAsIndividualAsync(Guid documentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default);
}

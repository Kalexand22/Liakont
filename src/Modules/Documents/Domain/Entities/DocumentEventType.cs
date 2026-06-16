namespace Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Type d'un <see cref="DocumentEvent"/> de la piste d'audit (F06 §3). À la GENÈSE
/// (<see cref="DocumentDetected"/>) s'ajoutent, avec la machine à états (TRK02), un type par OUTCOME de
/// transition (l'état atteint) : la piste d'audit rejoue ainsi la chronologie complète du document. Les
/// types liés à l'altération source après émission (TRK03) et aux snapshots d'émission/rejet (TRK04)
/// s'ajoutent avec ces items.
/// </summary>
/// <remarks>
/// Persisté en TEXTE (nom de l'énumération) dans la colonne <c>event_type</c> — même motif de lisibilité
/// d'audit que <see cref="DocumentState"/>. L'état de PROVENANCE de la transition reste tracé dans le
/// <c>Detail</c> de l'événement.
/// </remarks>
public enum DocumentEventType
{
    /// <summary>Document détecté/créé en état <c>Detected</c> par l'ingestion (genèse de la piste d'audit).</summary>
    DocumentDetected,

    /// <summary>Document passé en état <c>Blocked</c> (une validation pré-envoi l'a refusé).</summary>
    DocumentBlocked,

    /// <summary>Document passé en état <c>ReadyToSend</c> (depuis <c>Detected</c>, <c>Blocked</c> après correction, ou reprise <c>TechnicalError</c>).</summary>
    DocumentReadyToSend,

    /// <summary>Document passé en état <c>Sending</c> (transmission engagée).</summary>
    DocumentSending,

    /// <summary>Document passé en état <c>Issued</c> (accepté par la Plateforme Agréée).</summary>
    DocumentIssued,

    /// <summary>Document passé en état <c>RejectedByPa</c> (rejeté par la Plateforme Agréée).</summary>
    DocumentRejectedByPa,

    /// <summary>Document passé en état <c>TechnicalError</c> (erreur technique de transmission, re-tentable).</summary>
    DocumentTechnicalError,

    /// <summary>Document passé en état terminal <c>Superseded</c> (remplacé par un nouveau document après rejet).</summary>
    DocumentSuperseded,

    /// <summary>Document passé en état terminal <c>ManuallyHandled</c> (traité manuellement hors passerelle, motif journalisé).</summary>
    DocumentManuallyHandled,

    /// <summary>
    /// Altération de la source détectée APRÈS émission (item TRK03, F06 §3 « double usage du
    /// payload_hash ») : une <c>source_reference</c> déjà émise a été re-soumise avec une empreinte
    /// différente. Fait d'audit append-only inscrit SUR le document émis ; jamais de réémission ni de
    /// mise à jour du document émis (DR6 point 2 : toute altération laisse une trace horodatée).
    /// </summary>
    DocumentSourceAlteredAfterIssue,

    /// <summary>
    /// PDF du pool non lié rapproché AUTOMATIQUEMENT du document (item TRK07, décision
    /// 2026-06-02) : correspondance de CONFIANCE HAUTE (numéro de document trouvé dans le nom de
    /// fichier ou le texte du PDF), liée sans intervention. Le PDF rejoint le paquet d'archive en
    /// addendum chaîné (WORM, TRK05). Événement SYSTÈME (aucun opérateur).
    /// </summary>
    DocumentReconciledAuto,

    /// <summary>
    /// PDF du pool non lié rapproché MANUELLEMENT du document par un opérateur (item TRK07) :
    /// proposition de confiance moyenne (date + montant) confirmée, ou rattachement manuel
    /// d'un PDF orphelin. Porte l'identité de l'opérateur. Le PDF rejoint le paquet d'archive en
    /// addendum chaîné (WORM, TRK05).
    /// </summary>
    DocumentReconciledManual,

    /// <summary>
    /// Verdict OPÉRATEUR du garde-fou B2B/B2C (item API02b, F08 §A.4) : l'opérateur a confirmé que
    /// l'acheteur est un PARTICULIER (B2C) malgré l'indice professionnel détecté (VAL05). Fait d'audit
    /// append-only portant l'identité de l'opérateur ; il NE change PAS l'état du document (la re-vérification
    /// le débloque ensuite). La décision tranchée prime sur l'heuristique d'indice à la re-validation.
    /// </summary>
    DocumentBuyerConfirmedB2C,

    /// <summary>
    /// Re-vérification OPÉRATEUR ayant laissé le document BLOQUÉ (item FIX02, recette GATE_CONSOLE_WEB) :
    /// l'opérateur a déclenché une re-vérification (recheck) mais le document reste <c>Blocked</c>. Fait
    /// d'audit append-only portant l'identité de l'opérateur et le motif RÉÉVALUÉ (dans le <c>Detail</c>) :
    /// l'action n'est plus invisible dans la piste (F06 §3) et le motif courant affiché est le DERNIER évalué.
    /// NE change PAS l'état (la machine à états interdit <c>Blocked → Blocked</c>) : aucune transition, juste
    /// la trace du geste opérateur et de son résultat.
    /// </summary>
    DocumentRecheckedStillBlocked,

    /// <summary>
    /// Journalisation de l'envoi à la Plateforme Agréée (item FX06, F16 §7) : trace, en colonnes explicites
    /// et recherchables, le COMPTE / PLUG-IN de la PA, les horodatages requête/réponse, l'EMPREINTE de
    /// l'artefact transmis (le Factur-X) et la CLÉ d'idempotence de l'envoi ; la réponse PA brute reste portée
    /// par <c>pa_response_snapshot</c>. Fait d'audit append-only SYSTÈME (la transmission n'est pas une action
    /// opérateur). N'emporte AUCUNE transition d'état : c'est le détail de support/traçabilité de l'envoi, posé
    /// par le pipeline au moment de la finalisation (FX07), pas une décision de la machine à états.
    /// </summary>
    DocumentPaTransmissionJournaled,
}

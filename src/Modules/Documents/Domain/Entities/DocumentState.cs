namespace Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// États d'un document dans la passerelle (F06 §3). Le document est créé en <see cref="Detected"/>
/// par l'ingestion (PIV04, item TRK01) ; les transitions autorisées entre ces états sont portées par la
/// MACHINE À ÉTATS explicite (<c>DocumentStateMachine</c>, item TRK02). <see cref="Superseded"/> et
/// <see cref="ManuallyHandled"/> sont les deux états TERMINAUX : un document n'en sort jamais (il reste
/// visible et auditable, mais ne pollue plus les compteurs d'attente).
/// </summary>
/// <remarks>
/// La valeur est persistée en TEXTE (nom de l'énumération) dans la colonne <c>state</c> : un état est
/// une donnée d'audit fiscal, et un libellé lisible survit à un renumérotage de l'énumération et reste
/// interprétable par un vérificateur lisant directement la base (F06 §2, exigence de lisibilité).
/// </remarks>
public enum DocumentState
{
    /// <summary>Document reçu de la source, pas encore mappé ni validé (état initial — ingestion PIV04).</summary>
    Detected,

    /// <summary>Bloqué : une validation pré-envoi l'a refusé ; ne sera pas transmis tant qu'il n'est pas corrigé.</summary>
    Blocked,

    /// <summary>Prêt à être transmis à la Plateforme Agréée.</summary>
    ReadyToSend,

    /// <summary>Transmission en cours.</summary>
    Sending,

    /// <summary>Émis : accepté par la Plateforme Agréée (preuve de transmission disponible).</summary>
    Issued,

    /// <summary>Rejeté par la Plateforme Agréée.</summary>
    RejectedByPa,

    /// <summary>Erreur technique de transmission, re-tentable au prochain traitement.</summary>
    TechnicalError,

    /// <summary>Remplacé par un nouveau document (état TERMINAL — mécanique de remplacement après rejet, TRK02).</summary>
    Superseded,

    /// <summary>
    /// Traité manuellement hors passerelle par un opérateur (état TERMINAL, motif obligatoire et journalisé,
    /// TRK02) — cas d'un avoir orphelin ou d'un document non transmissible.
    /// </summary>
    ManuallyHandled,
}

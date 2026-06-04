namespace Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// États d'un document dans la passerelle (F06 §3). Le document est créé en <see cref="Detected"/>
/// par l'ingestion (PIV04, item TRK01) ; la MACHINE À ÉTATS (transitions autorisées, supersede,
/// traitement manuel) et l'état terminal <c>ManuallyHandled</c> arrivent avec TRK02 — ils ne sont pas
/// définis ici pour rester dans le périmètre « schéma + Document/DocumentEvent » de TRK01.
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

    /// <summary>Remplacé par un nouveau document (état terminal — mécanique de remplacement TRK02).</summary>
    Superseded,
}

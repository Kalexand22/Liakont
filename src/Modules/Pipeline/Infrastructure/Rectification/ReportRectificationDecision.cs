namespace Liakont.Modules.Pipeline.Infrastructure.Rectification;

/// <summary>
/// Décision prise par <see cref="ReportRectificationService"/> pour une période (PIP04, flux RE). Décrit ce
/// que la tentative a effectivement FAIT — base du compte rendu opérateur (CLAUDE.md n°12) et de l'idempotence.
/// </summary>
public enum ReportRectificationDecision
{
    /// <summary>Période sans donnée reportable — aucun envoi (déclaration « néant » non requise par défaut, F09 §5.4), aucune entrée.</summary>
    NothingToDeclare,

    /// <summary>Contenu inchangé depuis la dernière entrée pertinente — IDEMPOTENT, aucun envoi, aucune entrée (PIP04 §4).</summary>
    NoChange,

    /// <summary>Rectificatif transmis et accepté par la Plateforme Agréée (annule-et-remplace effectif).</summary>
    Transmitted,

    /// <summary>Capacité de rectification (flux RE) absente — agrégat conservé EN ATTENTE, aucun envoi (PIP04 §2).</summary>
    PendingCapability,

    /// <summary>Rectificatif rejeté par la Plateforme Agréée (errors[]).</summary>
    RejectedByPa,

    /// <summary>Erreur technique de transmission — re-tentable au prochain cycle.</summary>
    TechnicalError,
}

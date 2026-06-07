namespace Liakont.Modules.Pipeline.Domain.Rectification;

/// <summary>
/// Issue d'un rectificatif d'e-reporting (PIP04, flux RE) consignée dans le journal append-only
/// <c>pipeline.report_rectifications</c>. Distincte de la qualification fiscale d'agrégation
/// (<c>PaymentAggregationStatus</c>) et de la machine à états de transmission du module Payments
/// (<c>PaymentAggregateState</c>) : décrit l'OUTCOME d'une TENTATIVE de rectification. Persistée par NOM
/// (lisibilité d'audit).
/// </summary>
public enum ReportRectificationStatus
{
    /// <summary>Rectificatif transmis et accepté par la Plateforme Agréée (annule-et-remplace effectif).</summary>
    Transmitted,

    /// <summary>EN ATTENTE : la Plateforme Agréée ne déclare pas (encore) la capacité de rectification (flux RE) — aucun envoi.</summary>
    PendingCapability,

    /// <summary>Rectificatif rejeté par la Plateforme Agréée (errors[] non vides) — pas de retry automatique.</summary>
    RejectedByPa,

    /// <summary>Erreur technique de transmission — re-tentable au prochain cycle.</summary>
    TechnicalError,
}

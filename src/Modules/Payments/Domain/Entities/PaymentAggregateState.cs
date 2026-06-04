namespace Liakont.Modules.Payments.Domain.Entities;

/// <summary>
/// État de TRANSMISSION d'un <see cref="PaymentAggregate"/> (F06 §3 / F09, item TRK04). État OPÉRATIONNEL
/// (cycle de transmission de l'agrégat à la Plateforme Agréée), pas une qualification fiscale : il décrit
/// où en est l'envoi, jamais comment l'agrégat est calculé (calcul = PIP03). Persisté en TEXTE (nom de
/// l'énumération) — lisibilité d'audit, comme <c>DocumentState</c>.
/// </summary>
public enum PaymentAggregateState
{
    /// <summary>Agrégat calculé par le pipeline (PIP03), prêt à transmettre — état initial.</summary>
    Calculated,

    /// <summary>Transmission de l'agrégat à la Plateforme Agréée engagée.</summary>
    Sending,

    /// <summary>Agrégat transmis et accepté par la Plateforme Agréée (état terminal d'envoi réussi).</summary>
    Transmitted,

    /// <summary>Agrégat rejeté par la Plateforme Agréée (état terminal : un nouvel agrégat sera recalculé).</summary>
    RejectedByPa,

    /// <summary>Erreur technique de transmission, re-tentable (TechnicalError → Sending au prochain traitement).</summary>
    TechnicalError,
}

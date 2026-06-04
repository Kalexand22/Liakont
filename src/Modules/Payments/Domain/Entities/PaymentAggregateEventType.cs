namespace Liakont.Modules.Payments.Domain.Entities;

/// <summary>
/// Type d'un <see cref="PaymentAggregateEvent"/> de la piste d'audit des agrégats de paiement (F06 §3 / F09,
/// item TRK04). À la GENÈSE (<see cref="AggregateCalculated"/>) s'ajoute un type par OUTCOME de transition
/// de transmission : la piste d'audit rejoue la chronologie complète de l'agrégat transmis à la DGFiP, qui a
/// la même valeur fiscale qu'un document. Persisté en TEXTE (nom de l'énumération) — lisibilité d'audit.
/// </summary>
public enum PaymentAggregateEventType
{
    /// <summary>Agrégat calculé par le pipeline (PIP03) en état <c>Calculated</c> (genèse de la piste d'audit).</summary>
    AggregateCalculated,

    /// <summary>Agrégat passé en état <c>Sending</c> (transmission engagée).</summary>
    AggregateSending,

    /// <summary>Agrégat passé en état <c>Transmitted</c> (accepté par la Plateforme Agréée).</summary>
    AggregateTransmitted,

    /// <summary>Agrégat passé en état <c>RejectedByPa</c> (rejeté par la Plateforme Agréée).</summary>
    AggregateRejectedByPa,

    /// <summary>Agrégat passé en état <c>TechnicalError</c> (erreur technique de transmission, re-tentable).</summary>
    AggregateTechnicalError,
}

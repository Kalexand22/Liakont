namespace Liakont.Modules.Pipeline.Domain.Payments;

/// <summary>
/// Qualification FISCALE d'un agrégat jour×taux de paiement calculé par PIP03a — distincte de l'état
/// OPÉRATIONNEL de transmission (<c>PaymentAggregateState</c> du module Payments, INV-PAYMENTS-007, qui
/// décrit l'envoi, pas la qualification). Décrit POURQUOI un agrégat calculé est, ou non, transmissible —
/// le fenêtrage en période et l'envoi réel restent PIP03b. Aucun agrégat n'est transmis par PIP03a.
/// </summary>
public enum PaymentAggregationStatus
{
    /// <summary>Calculé et transmissible (tous les paramètres fiscaux renseignés + capacité PA présente) — en attente du fenêtrage/envoi (PIP03b).</summary>
    Calculated,

    /// <summary>Calculé pour la traçabilité mais transmission SUSPENDUE (décision fiscale de l'expert-comptable en attente).</summary>
    Suspended,

    /// <summary>Calculé pour la traçabilité mais NON REQUIS (TVA sur les débits — exigibilité à la facturation, pas d'e-reporting de paiement).</summary>
    NotRequired,

    /// <summary>Calculé mais EN ATTENTE : la Plateforme Agréée ne déclare pas encore la capacité de transmission des paiements (Flux 10.4).</summary>
    PendingCapability,

    /// <summary>
    /// La SOURCE n'expose pas d'encaissements (capacité <c>ExposesPayments</c> non déclarée, RD403) :
    /// l'e-reporting de paiement n'est pas applicable à cette source. À DISTINGUER d'une source qui
    /// expose les encaissements mais n'en a aucun sur la période (« zéro encaissement » → <see cref="Calculated"/>,
    /// agrégats vides) : ici on ne transmet jamais un néant à tort (ADR-0004 D2 : flux 10.4 conditionné à la capacité).
    /// </summary>
    SourceWithoutPayments,
}

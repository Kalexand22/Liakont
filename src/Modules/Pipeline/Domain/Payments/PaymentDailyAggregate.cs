namespace Liakont.Modules.Pipeline.Domain.Payments;

using System;

/// <summary>
/// Agrégat jour×taux de l'e-reporting de paiement calculé par PIP03a (F09 §2) : pour un jour d'encaissement
/// et un taux de TVA, la base taxable HT et la TVA encaissées, plus la qualification fiscale
/// (<see cref="PaymentAggregationStatus"/>). PRÉ-fenêtrage : aucun rattachement à une période déclarative
/// (D-a non tranchée → PIP03b). Montants en <see cref="decimal"/> (CLAUDE.md n°1), pouvant être négatifs
/// (remboursement — F09 §5.4).
/// </summary>
public sealed record PaymentDailyAggregate
{
    /// <summary>Jour d'encaissement agrégé.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Taux de TVA de la ventilation (decimal).</summary>
    public required decimal Rate { get; init; }

    /// <summary>Base taxable HT encaissée du jour pour ce taux (decimal, peut être négative).</summary>
    public required decimal TaxableBase { get; init; }

    /// <summary>TVA encaissée du jour pour ce taux (decimal, peut être négative).</summary>
    public required decimal VatAmount { get; init; }

    /// <summary>Qualification fiscale de l'agrégat (transmissible / suspendu / non requis / capacité en attente).</summary>
    public required PaymentAggregationStatus Status { get; init; }

    /// <summary>Motif opérateur quand l'agrégat n'est pas transmissible (CLAUDE.md n°12) ; <c>null</c> si Calculated.</summary>
    public string? Reason { get; init; }
}

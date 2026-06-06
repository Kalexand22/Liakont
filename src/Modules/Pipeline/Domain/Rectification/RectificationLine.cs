namespace Liakont.Modules.Pipeline.Domain.Rectification;

using System;

/// <summary>
/// Ligne d'un agrégat rectifié (PIP04, flux RE) : pour un jour d'encaissement et un taux de TVA, la base
/// taxable HT et la TVA encaissées qui composent la déclaration COMPLÈTE de la période (annule-et-remplace —
/// F07-F08 §B.1). Reprise FIDÈLE d'un <c>PaymentDailyAggregate</c> reportable de la projection PIP03a : aucun
/// recalcul, aucune dérivation (CLAUDE.md n°2). Montants en <see cref="decimal"/> (CLAUDE.md n°1), pouvant
/// être négatifs (remboursement — F09 §5.4).
/// </summary>
public sealed record RectificationLine
{
    /// <summary>Jour d'encaissement agrégé.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Taux de TVA de la ventilation (decimal).</summary>
    public required decimal Rate { get; init; }

    /// <summary>Base taxable HT encaissée du jour pour ce taux (decimal, peut être négative).</summary>
    public required decimal TaxableBase { get; init; }

    /// <summary>TVA encaissée du jour pour ce taux (decimal, peut être négative).</summary>
    public required decimal VatAmount { get; init; }
}

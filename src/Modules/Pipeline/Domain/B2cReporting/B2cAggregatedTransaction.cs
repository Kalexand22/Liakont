namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System;
using System.Collections.Generic;

/// <summary>
/// Transaction e-reporting B2C agrégée N→1 (flux 10.3) au grain <b>jour × devise</b> (catégorie et rôle
/// fixés par l'appelant — TMA1 / SE pour les enchères, F03 §2.5). Porte ses <see cref="Subtotals"/> par
/// taux et la liste des <see cref="Contributions"/> (réversibilité « retrouver les N pièces »). Tout en
/// <see cref="decimal"/> (CLAUDE.md n°1).
/// </summary>
public sealed record B2cAggregatedTransaction
{
    /// <summary>Jour de l'agrégat.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Devise ISO 4217.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>Montant total HT (TT-82) = Σ des bases HT des <see cref="Subtotals"/>.</summary>
    public required decimal TaxExclusiveAmount { get; init; }

    /// <summary>Montant total de TVA (TT-83) = Σ des TVA des <see cref="Subtotals"/>.</summary>
    public required decimal TaxTotal { get; init; }

    /// <summary>Sous-totaux par taux (au moins un).</summary>
    public required IReadOnlyList<B2cAggregatedSubtotal> Subtotals { get; init; }

    /// <summary>Documents source ayant alimenté cet agrégat (traçabilité N→1).</summary>
    public required IReadOnlyList<B2cContributionRef> Contributions { get; init; }
}

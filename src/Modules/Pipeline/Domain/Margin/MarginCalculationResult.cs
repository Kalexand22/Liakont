namespace Liakont.Modules.Pipeline.Domain.Margin;

using System.Collections.Generic;

/// <summary>
/// Résultat du calcul de la marge e-reporting B2C (B2C-09b) : la marge PAR LOT (no_ba) + le total.
/// Le type ne porte AUCUN champ de TVA : le montant de marge est une BASE (art. 297 E) — la
/// non-séparabilité de la TVA est imposée à la source par <see cref="MarginCalculator"/> (garde 297 E
/// bloquante). La transmission de ce montant (cas DGFiP n°33) est l'affaire de B2C-09c, pas d'ici.
/// </summary>
public sealed record MarginCalculationResult
{
    /// <summary>Marges par lot, ordre déterministe (1re apparition d'un lot dans les frais).</summary>
    public required IReadOnlyList<LotMargin> Lots { get; init; }

    /// <summary>Σ des marges de tous les lots, decimal half-up. <c>0</c> si aucun frais.</summary>
    public required decimal TotalMargin { get; init; }
}

namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Sous-total par taux d'une <see cref="B2cAggregatedTransaction"/> (TG-32 : TT-86/87/88). La marge TTC
/// agrégée du taux est ramenée HT : <c>HT = arrondi(TTC / (1 + taux))</c>, <c>TVA = TTC − HT</c> (arrondi
/// commercial half-up, F03 §2.5). Tout en <see cref="decimal"/> (CLAUDE.md n°1).
/// </summary>
public sealed record B2cAggregatedSubtotal
{
    /// <summary>Taux de TVA (TT-86), pourcentage.</summary>
    public required decimal RatePercent { get; init; }

    /// <summary>Base imposable = marge ramenée HT (TT-87).</summary>
    public required decimal TaxableAmount { get; init; }

    /// <summary>TVA sur la marge (TT-88).</summary>
    public required decimal TaxTotal { get; init; }
}

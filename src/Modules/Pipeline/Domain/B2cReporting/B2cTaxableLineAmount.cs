namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Montant d'UNE ligne d'adjudication taxable (entrée PURE de <see cref="B2cTaxableResolver"/>), au régime du
/// prix total (F03 §2.7). HT et TVA sont SOURCÉS (lus sur la ventilation du CHECK — jamais recalculés, cf.
/// <c>CheckTvaMapping</c>) ; le taux est le taux mappé de la ligne (<c>null</c> si non résolu → le résolveur
/// bloque, jamais deviné, CLAUDE.md n°2/3). Tout en <see cref="decimal"/> (CLAUDE.md n°1).
/// </summary>
public sealed record B2cTaxableLineAmount
{
    /// <summary>Taux de TVA mappé (pourcentage, ex. <c>20.0</c>) ; <c>null</c> si non résolu (→ blocage).</summary>
    public required decimal? RatePercent { get; init; }

    /// <summary>Base HT sourcée de l'adjudication (jamais recalculée).</summary>
    public required decimal TaxableHt { get; init; }

    /// <summary>TVA sourcée de l'adjudication (jamais recalculée).</summary>
    public required decimal TaxVat { get; init; }
}

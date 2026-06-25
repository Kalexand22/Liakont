namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Composante d'UN document taxable à un taux donné, produite par <see cref="B2cTaxableResolver"/> (F03 §2.7).
/// Sépare la part ADJUDICATION (HT + TVA SOURCÉS, jamais recalculés) de la part COMMISSION ACHETEUR
/// (<see cref="HonoraireTtc"/>, TTC, dont la conversion HT est DIFFÉRÉE à l'agrégat du jour pour éviter toute
/// dérive d'arrondi — cohérent avec la marge, F03 §2.5). Tout en <see cref="decimal"/> (CLAUDE.md n°1).
/// </summary>
public sealed record B2cTaxableRateComponent
{
    /// <summary>Taux de TVA (pourcentage, ex. <c>20.0</c>).</summary>
    public required decimal RatePercent { get; init; }

    /// <summary>Base HT sourcée de l'adjudication à ce taux (jamais recalculée).</summary>
    public required decimal AdjudicationHt { get; init; }

    /// <summary>TVA sourcée de l'adjudication à ce taux (jamais recalculée).</summary>
    public required decimal AdjudicationVat { get; init; }

    /// <summary>Commission acheteur TTC à ce taux (ramenée HT à l'agrégat — F03 §2.5/§2.7).</summary>
    public required decimal HonoraireTtc { get; init; }
}

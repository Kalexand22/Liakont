namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System;

/// <summary>
/// Contribution d'UN document B2C taxable à l'agrégat e-reporting (flux 10.3, catégorie TLB1) à un taux donné.
/// Donnée d'entrée PURE de <see cref="B2cTaxableAggregationCalculator"/> : la composante par taux du document
/// (<see cref="B2cTaxableRateComponent"/>) augmentée de son identité (jour, devise, pièce source). L'adjudication
/// est HT/TVA SOURCÉE ; la commission acheteur reste TTC (ramenée HT à l'agrégat). Tout en <see cref="decimal"/>
/// (CLAUDE.md n°1).
/// </summary>
public sealed record B2cTaxableContribution
{
    /// <summary>Identifiant du document source (déclaration B2C taxable).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Référence de la pièce source (ADR-0007) — traçabilité reporting ↔ pièces.</summary>
    public required string SourceReference { get; init; }

    /// <summary>Jour de l'opération (grain d'agrégation, F03 §2.5).</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Devise ISO 4217 (ex. <c>EUR</c>).</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>Taux de TVA (pourcentage).</summary>
    public required decimal RatePercent { get; init; }

    /// <summary>Base HT sourcée de l'adjudication à ce taux (jamais recalculée).</summary>
    public required decimal AdjudicationHt { get; init; }

    /// <summary>TVA sourcée de l'adjudication à ce taux (jamais recalculée).</summary>
    public required decimal AdjudicationVat { get; init; }

    /// <summary>Commission acheteur TTC à ce taux (ramenée HT à l'agrégat — F03 §2.5/§2.7).</summary>
    public required decimal HonoraireTtc { get; init; }
}

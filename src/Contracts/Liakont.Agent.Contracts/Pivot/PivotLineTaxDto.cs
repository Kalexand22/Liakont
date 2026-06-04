namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Ventilation de TVA d'une ligne (EN 16931 BG-30). Les montants viennent de la source (le pivot
/// ne calcule rien — F01-F02 §3.7 règle 2). La <see cref="CategoryCode"/> et le
/// <see cref="VatexCode"/> sont le RÉSULTAT du mapping TVA de la PLATEFORME (lot F03) : l'agent
/// transmet le régime source brut (<see cref="PivotLineDto.SourceRegimeCodes"/>) et laisse ces
/// deux champs nuls (note v6 PIV01 : la trace de mapping n'est pas dans le contrat).
/// </summary>
public sealed class PivotLineTaxDto
{
    /// <summary>Crée une ventilation de TVA de ligne.</summary>
    /// <param name="taxAmount">Montant de TVA de la ligne (decimal), calculé par la source.</param>
    /// <param name="rate">Taux de TVA en pourcentage (EN 16931 BT-152). Absent = <c>null</c>.</param>
    /// <param name="categoryCode">
    /// Catégorie UNCL5305 (EN 16931 BT-151) — RÉSULTAT du mapping plateforme, nul tant que non mappé.
    /// </param>
    /// <param name="vatexCode">
    /// Code VATEX d'exonération (EN 16931 BT-121) — obligatoire si catégorie E et taux 0
    /// (contrôlé par Validation, pas ici). Absent = <c>null</c>.
    /// </param>
    public PivotLineTaxDto(
        decimal taxAmount,
        decimal? rate = null,
        VatCategory? categoryCode = null,
        string? vatexCode = null)
    {
        TaxAmount = taxAmount;
        Rate = rate;
        CategoryCode = categoryCode;
        VatexCode = vatexCode;
    }

    /// <summary>Montant de TVA de la ligne (decimal), calculé par la source.</summary>
    public decimal TaxAmount { get; }

    /// <summary>Taux de TVA en pourcentage (EN 16931 BT-152).</summary>
    public decimal? Rate { get; }

    /// <summary>Catégorie UNCL5305 (EN 16931 BT-151) — résultat du mapping plateforme.</summary>
    public VatCategory? CategoryCode { get; }

    /// <summary>Code VATEX d'exonération (EN 16931 BT-121).</summary>
    public string? VatexCode { get; }
}

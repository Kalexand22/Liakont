namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Totaux de contrôle du document (EN 16931 BG-22). Comparés à la somme des lignes par la
/// Validation (F04, règle BR-CO-15) — le pivot ne calcule rien (F01-F02 §3.7 règle 2).
/// </summary>
public sealed class PivotTotalsDto
{
    /// <summary>Crée les totaux pivot.</summary>
    /// <param name="totalNet">Total HT (EN 16931 BT-109), decimal.</param>
    /// <param name="totalTax">Total TVA (EN 16931 BT-110), decimal.</param>
    /// <param name="totalGross">Total TTC (EN 16931 BT-112), decimal.</param>
    /// <param name="sourceTotalGross">
    /// Total TTC tel que stocké par la source (contrôle de cohérence d'extraction). OPTIONNEL :
    /// certaines sources ne stockent pas de total d'entête ou le ventilent par taux (ADR-0004 D3-5).
    /// </param>
    public PivotTotalsDto(
        decimal totalNet,
        decimal totalTax,
        decimal totalGross,
        decimal? sourceTotalGross = null)
    {
        TotalNet = totalNet;
        TotalTax = totalTax;
        TotalGross = totalGross;
        SourceTotalGross = sourceTotalGross;
    }

    /// <summary>Total HT (EN 16931 BT-109).</summary>
    public decimal TotalNet { get; }

    /// <summary>Total TVA (EN 16931 BT-110).</summary>
    public decimal TotalTax { get; }

    /// <summary>Total TTC (EN 16931 BT-112).</summary>
    public decimal TotalGross { get; }

    /// <summary>Total TTC brut de la source (contrôle de cohérence), optionnel.</summary>
    public decimal? SourceTotalGross { get; }
}

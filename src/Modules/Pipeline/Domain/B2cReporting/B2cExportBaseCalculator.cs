namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System.Linq;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Cœur PUR (sans I/O) du calcul de la BASE HT d'une déclaration B2C d'export hors UE détaxé (flux 10.3,
/// art. 262 I — F03 §2.8). La base est le total HT exonéré de l'opération : adjudication(s) HT (montants des
/// lignes, SOURCÉS) + commission acheteur. Sous un export hors UE la commission est exonérée elle aussi — le
/// code source ne porte AUCUNE TVA de frais sur un export (F03 §2.8, vérifié sur la donnée) : son montant TTC
/// EST le HT, aucun « ramené HT » n'est appliqué (à la différence du prix total taxable). La <b>commission
/// vendeur</b> est EXCLUE (jambe B2B, prestation office→vendeur) : le bordereau acheteur (BA) ne porte que la
/// commission acheteur. Tout en <see cref="decimal"/> (CLAUDE.md n°1) ; AUCUN montant n'est recalculé ni inventé.
/// </summary>
public static class B2cExportBaseCalculator
{
    /// <summary>
    /// Calcule la base HT exonérée (TT-82, au taux 0) d'un document export : Σ des montants HT des lignes
    /// (adjudication) + Σ des commissions acheteur. Les frais vendeur ne sont PAS comptés (jambe B2B, F03 §2.8).
    /// </summary>
    /// <param name="pivot">Le pivot du document export (enrichi par le mapping TVA).</param>
    /// <returns>La base HT exonérée, en <see cref="decimal"/>.</returns>
    public static decimal ComputeTaxExclusiveBase(PivotDocumentDto pivot)
    {
        decimal adjudicationHt = pivot.Lines.Sum(line => line.NetAmount);
        decimal commissionHt = (pivot.BuyerFees ?? Enumerable.Empty<PivotBuyerFeeDto>()).Sum(fee => fee.NetAmount);
        return adjudicationHt + commissionHt;
    }
}

namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System.Linq;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Cœur PUR (sans I/O) du calcul de la BASE HT d'une déclaration B2C d'export hors UE détaxé (flux 10.3,
/// art. 262 I — F03 §2.8). La base est le total HT exonéré de l'opération : adjudication(s) HT (montants des
/// lignes, SOURCÉS) + commission acheteur HT. La commission est portée TTC (<see cref="PivotBuyerFeeDto.NetAmount"/>) ;
/// son HT est recouvré PAR CONSTRUCTION en retranchant la TVA de frais SOURCE
/// (<see cref="PivotBuyerFeeDto.SourceTaxAmount"/>, brute). Sous un export la commission est exonérée — la TVA de
/// frais y vaut 0 (F03 §2.8, vérifié sur la donnée), donc TTC = HT ; mais on ne S'APPUIE PLUS sur cet invariant
/// implicite : si la source portait une TVA de frais, elle serait retranchée et la base resterait juste. La
/// <b>commission vendeur</b> est EXCLUE (jambe B2B, prestation office→vendeur) : le bordereau acheteur (BA) ne
/// porte que la commission acheteur. Tout en <see cref="decimal"/> (CLAUDE.md n°1) ; AUCUN montant n'est recalculé
/// ni inventé (la TVA de frais n'est pas dérivée d'un taux, elle vient telle quelle de la source).
/// </summary>
public static class B2cExportBaseCalculator
{
    /// <summary>
    /// Calcule la base HT exonérée (TT-82, au taux 0) d'un document export : Σ des montants HT des lignes
    /// (adjudication) + Σ des commissions acheteur HT (TTC <see cref="PivotBuyerFeeDto.NetAmount"/> − TVA de frais
    /// source <see cref="PivotBuyerFeeDto.SourceTaxAmount"/>). Les frais vendeur ne sont PAS comptés (jambe B2B, F03 §2.8).
    /// </summary>
    /// <param name="pivot">Le pivot du document export (enrichi par le mapping TVA).</param>
    /// <returns>La base HT exonérée, en <see cref="decimal"/>.</returns>
    public static decimal ComputeTaxExclusiveBase(PivotDocumentDto pivot)
    {
        decimal adjudicationHt = pivot.Lines.Sum(line => line.NetAmount);
        decimal commissionHt = (pivot.BuyerFees ?? Enumerable.Empty<PivotBuyerFeeDto>())
            .Sum(fee => fee.NetAmount - (fee.SourceTaxAmount ?? 0m));
        return adjudicationHt + commissionHt;
    }
}

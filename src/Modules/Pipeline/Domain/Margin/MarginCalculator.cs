namespace Liakont.Modules.Pipeline.Domain.Margin;

using System.Collections.Generic;
using System.Linq;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Cœur PUR (sans I/O) du calcul de la marge e-reporting B2C (B2C-09b). Applique la formule ANCRÉE et
/// validée (GATE_B2C_SOURCING, F03 §2.4 sur CGI art. 297 A I-2° + BOI-TVA-SECT-90-50 §270) :
/// <c>marge = Σ frais acheteur + Σ frais vendeur</c>, au grain LOT (no_ba), méthode PAR OPÉRATION unique
/// (aucun enum de méthode — la globalisation n'est pas ancrée pour l'OVV agissant en son nom propre).
/// <para>
/// ⚠️ <b>SUPERSÉDÉ — ne pas câbler tel quel.</b> Depuis l'ancrage F03 §2.5, le frais source
/// (<see cref="PivotBuyerFeeDto.NetAmount"/>) est porté en <b>TTC</b> (enchères) : la somme produite ici
/// (<see cref="MarginCalculationResult.TotalMargin"/> / <see cref="LotMargin.MarginAmount"/>) est donc un
/// montant <b>TTC</b>, PAS une base HT reportable. La voie de production effective de l'e-reporting B2C est
/// <c>B2cMarginResolver</c> → <c>B2cTransactionAggregationCalculator</c> (ramène le TTC en HT,
/// <c>HT = arrondi(TTC / (1 + taux))</c>). Ce type n'a AUCUN consommateur de production : NE PAS consommer
/// <c>TotalMargin</c>/<c>MarginAmount</c> comme base HT (sur-déclarerait la base de TVA sur la marge — n°1/n°2).
/// <para>⚠️ <b>Doublement obsolète depuis BUG-17 (volet b).</b> L'honoraire ACHETEUR est désormais porté en LIGNE
/// (rôle <see cref="PivotLineRole.BuyerFee"/>), plus dans <see cref="PivotDocumentDto.BuyerFees"/> (que l'agent
/// EncheresV6 ne peuple plus). Ce calculateur sommant encore <c>document.BuyerFees</c>, il SOUS-déclarerait la jambe
/// acheteur (= 0) s'il était recâblé. La voie vive lit la ligne via <c>B2cAuctionFeeLines.BuyerFeeLines</c>
/// (<c>B2cMarginAggregatorTenantJob</c>). À migrer ou supprimer si jamais réactivé — jamais consommer tel quel.</para>
/// </para>
/// </summary>
/// <remarks>
/// <para>AUCUNE règle fiscale inventée (CLAUDE.md n°2) : seuls les frais déjà portés en pivot (B2C-08 /
/// B2C-08c) sont sommés ; le 3e terme « impôts, droits, prélèvements et taxes » du §270 reste HORS marge
/// (F03 §2.4). Calcul en <see cref="decimal"/> exclusivement, arrondi commercial half-up à 2 décimales
/// (<see cref="PivotRounding.RoundAmount"/>, CLAUDE.md n°1).</para>
/// <para>CRITÈRE BLOQUANT art. 297 E : le montant de marge est une BASE — aucune TVA n'y figure
/// distinctement. Un document qui ferait apparaître une TVA distincte (total de TVA non nul, ou une ligne
/// portant une ventilation de TVA &gt; 0) fait ÉCHOUER le calcul (<see cref="MarginVatNotSeparableException"/>),
/// jamais une marge fausse (CLAUDE.md n°3). La TRANSMISSION du montant (cas DGFiP n°33) est l'affaire de
/// B2C-09c, pas d'ici.</para>
/// </remarks>
public static class MarginCalculator
{
    /// <summary>
    /// Calcule la marge par lot d'un document portant des frais acheteur / vendeur. Applique d'abord la
    /// garde 297 E (TVA distincte interdite), puis somme les frais par <c>LotReference</c> en decimal
    /// half-up. Un document sans aucun frais renvoie un résultat vide (Lots = [], TotalMargin = 0).
    /// Sortie déterministe : les lots sont ordonnés par leur 1re apparition dans les frais.
    /// </summary>
    /// <exception cref="MarginVatNotSeparableException">Le document fait apparaître une TVA distincte (art. 297 E).</exception>
    /// <exception cref="MarginLotReferenceMissingException">Un frais ne porte pas de référence de lot (no_ba null ou vide).</exception>
    public static MarginCalculationResult Calculate(PivotDocumentDto document)
    {
        System.ArgumentNullException.ThrowIfNull(document);

        EnsureNoSeparateVat(document);

        // Ordre déterministe : on enregistre les lots dans l'ordre de 1re apparition (frais vendeur puis
        // frais acheteur), et on agrège en decimal. Un même lot peut porter plusieurs frais (commission +
        // débours) et les deux jambes (acheteur et vendeur).
        var orderedLots = new List<string>();
        var sellerByLot = new Dictionary<string, decimal>(System.StringComparer.Ordinal);
        var buyerByLot = new Dictionary<string, decimal>(System.StringComparer.Ordinal);

        foreach (var fee in document.SellerFees ?? Enumerable.Empty<PivotSellerFeeDto>())
        {
            if (string.IsNullOrWhiteSpace(fee.LotReference))
            {
                throw MarginLotReferenceMissingException.ForDocument(document.Number);
            }

            Accumulate(orderedLots, sellerByLot, buyerByLot, fee.LotReference, sellerDelta: fee.NetAmount);
        }

        foreach (var fee in document.BuyerFees ?? Enumerable.Empty<PivotBuyerFeeDto>())
        {
            if (string.IsNullOrWhiteSpace(fee.LotReference))
            {
                throw MarginLotReferenceMissingException.ForDocument(document.Number);
            }

            Accumulate(orderedLots, sellerByLot, buyerByLot, fee.LotReference, buyerDelta: fee.NetAmount);
        }

        var lots = new List<LotMargin>(orderedLots.Count);
        decimal totalMargin = 0m;
        foreach (var lotReference in orderedLots)
        {
            var sellerTotal = PivotRounding.RoundAmount(sellerByLot[lotReference]);
            var buyerTotal = PivotRounding.RoundAmount(buyerByLot[lotReference]);
            var marginAmount = PivotRounding.RoundAmount(buyerTotal + sellerTotal);

            lots.Add(new LotMargin
            {
                LotReference = lotReference,
                BuyerFeesTotal = buyerTotal,
                SellerFeesTotal = sellerTotal,
                MarginAmount = marginAmount,
            });

            totalMargin += marginAmount;
        }

        return new MarginCalculationResult
        {
            Lots = lots,
            TotalMargin = PivotRounding.RoundAmount(totalMargin),
        };
    }

    /// <summary>
    /// Garde 297 E : le montant de marge est une BASE, aucune TVA distincte. Échoue si le total de TVA du
    /// document est non nul, ou si une ligne porte une ventilation de TVA &gt; 0. Une ligne exonérée
    /// (catégorie E, taux 0, <c>TaxAmount == 0</c> — l'adjudication sous le régime de la marge, F03 §2.3)
    /// ne viole PAS l'art. 297 E.
    /// </summary>
    private static void EnsureNoSeparateVat(PivotDocumentDto document)
    {
        if (document.Totals.TotalTax != 0m)
        {
            throw MarginVatNotSeparableException.ForDocument(document.Number);
        }

        if (document.Lines.Any(line => line.Taxes.Any(tax => tax.TaxAmount != 0m)))
        {
            throw MarginVatNotSeparableException.ForDocument(document.Number);
        }
    }

    private static void Accumulate(
        List<string> orderedLots,
        Dictionary<string, decimal> sellerByLot,
        Dictionary<string, decimal> buyerByLot,
        string lotReference,
        decimal sellerDelta = 0m,
        decimal buyerDelta = 0m)
    {
        if (!sellerByLot.ContainsKey(lotReference))
        {
            orderedLots.Add(lotReference);
            sellerByLot[lotReference] = 0m;
            buyerByLot[lotReference] = 0m;
        }

        sellerByLot[lotReference] += sellerDelta;
        buyerByLot[lotReference] += buyerDelta;
    }
}

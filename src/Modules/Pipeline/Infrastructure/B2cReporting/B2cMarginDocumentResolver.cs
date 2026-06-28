namespace Liakont.Modules.Pipeline.Infrastructure.B2cReporting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Liakont.Modules.TvaMapping.Contracts.Services;

/// <summary>
/// Résout la marge d'UN document marge (B2C ou B2B) : assemble la jambe ACHETEUR (lignes rôle <c>BuyerFee</c>, TTC =
/// net + TVA de ligne) + la jambe VENDEUR (<c>SellerFees</c>, décompte BV, TTC), résout le taux UNIQUE des honoraires
/// via le mapping F03 (<c>Part.Frais</c>) puis délègue au cœur PUR <see cref="B2cMarginResolver"/> (fail-closed,
/// art. 297 E). SOURCE DE VÉRITÉ UNIQUE de l'assemblage de la marge, partagée par le job d'e-reporting B2C
/// (<see cref="B2cMarginAggregatorTenantJob"/>) ET le récap marge du détail document — jamais une copie locale (un
/// écart = double-comptage ou marge fausse, P1). Le taux vient de la TABLE VALIDÉE du tenant, jamais inventé
/// (CLAUDE.md n°2). La conversion TTC→HT (pour le récap) reste dans <see cref="B2cTransactionAggregationCalculator.ToHt"/>.
/// </summary>
public static class B2cMarginDocumentResolver
{
    /// <summary>
    /// Résout la marge du document (ventilation acheteur/vendeur + marge TTC + taux), ou la bloque (fail-closed).
    /// </summary>
    /// <param name="tvaMapping">Service de mapping TVA du tenant (taux des honoraires, <c>Part.Frais</c>).</param>
    /// <param name="companyId">Société (tenant) courante.</param>
    /// <param name="pivot">Le pivot enrichi du document marge.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>La résolution de marge (résolue ou bloquée), avec la ventilation acheteur/vendeur.</returns>
    public static async Task<B2cMarginDocumentResolution> ResolveAsync(
        ITvaMappingService tvaMapping,
        Guid companyId,
        PivotDocumentDto pivot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tvaMapping);
        ArgumentNullException.ThrowIfNull(pivot);

        var sellerFees = pivot.SellerFees ?? Enumerable.Empty<PivotSellerFeeDto>();

        // Jambe VENDEUR (hors-lignes, décompte BV) + jambe ACHETEUR (lignes rôle BuyerFee). Montant TTC acheteur
        // recouvré PAR CONSTRUCTION = NetAmount + TVA de ligne (sous marge la ligne est pliée : net=TTC, TVA 0).
        // Sommes BRUTES (non arrondies) : la marge TTC est arrondie UNE fois (B2cMarginResolver) ; la ventilation
        // affichée est réconciliée plus bas pour sommer EXACTEMENT à la marge (jamais d'écart d'1 centime).
        var fees = new List<(decimal AmountTtc, string? RegimeCode, string? LineRef)>();
        decimal rawSellerTtc = 0m;
        foreach (var fee in sellerFees)
        {
            fees.Add((fee.NetAmount, fee.SourceRegimeCode, fee.SourceLineRef));
            rawSellerTtc += fee.NetAmount;
        }

        decimal rawBuyerTtc = 0m;
        foreach (var line in B2cAuctionFeeLines.BuyerFeeLines(pivot))
        {
            decimal amountTtc = line.NetAmount + line.Taxes.Sum(t => t.TaxAmount);
            rawBuyerTtc += amountTtc;
            fees.Add((amountTtc, line.SourceRegimeCodes.Count > 0 ? line.SourceRegimeCodes[0] : null, line.SourceLineRef));
        }

        var requests = fees
            .Select(f => new TvaLineMappingRequest
            {
                SourceRegimeCode = f.RegimeCode ?? string.Empty,
                Part = TvaMappingPart.Frais,
                LineRef = f.LineRef,
            })
            .ToList();

        var mapping = await tvaMapping.MapAsync(companyId, requests, cancellationToken);

        var honoraires = new List<B2cResolvedHonoraire>(fees.Count);
        for (var i = 0; i < fees.Count; i++)
        {
            // Index 1:1 requête→résultat ; table absente ou code non mappé → taux null → B2cMarginResolver bloque
            // (UnmappedRate), jamais un taux deviné.
            var line = mapping.TableExists && i < mapping.Lines.Count ? mapping.Lines[i] : null;
            var rate = line is { IsMapped: true } ? line.Rate : null;
            honoraires.Add(new B2cResolvedHonoraire { AmountTtc = fees[i].AmountTtc, RatePercent = rate });
        }

        var resolution = B2cMarginResolver.Resolve(HasSeparateVat(pivot), honoraires);

        // Ventilation affichée RÉCONCILIÉE par construction : acheteur arrondi normalement, vendeur = résiduel de la
        // marge (acheteur + vendeur == marge TTC, toujours). Pour des honoraires à 2 décimales (cas réel enchères),
        // identique à l'arrondi indépendant ; jamais d'écart d'1 centime dans le récap. Hors résolution (non affiché),
        // la jambe vendeur garde son arrondi naturel.
        var buyerFeesTtc = PivotRounding.RoundAmount(rawBuyerTtc);
        var sellerFeesTtc = resolution.IsResolved
            ? resolution.MarginTtc - buyerFeesTtc
            : PivotRounding.RoundAmount(rawSellerTtc);

        return new B2cMarginDocumentResolution
        {
            IsResolved = resolution.IsResolved,
            BuyerFeesTtc = buyerFeesTtc,
            SellerFeesTtc = sellerFeesTtc,
            MarginTtc = resolution.MarginTtc,
            RatePercent = resolution.RatePercent,
            BlockReason = resolution.BlockReason,
        };
    }

    /// <summary>
    /// Garde 297 E (miroir de <c>MarginCalculator.EnsureNoSeparateVat</c>) : le montant de marge est une BASE — aucune
    /// TVA distincte. TVA totale non nulle, ou une ligne portant une ventilation de TVA &gt; 0 → marge non séparable.
    /// </summary>
    /// <param name="pivot">Le pivot du document.</param>
    /// <returns><c>true</c> si le document fait apparaître une TVA distincte (marge non séparable).</returns>
    public static bool HasSeparateVat(PivotDocumentDto pivot) =>
        pivot.Totals.TotalTax != 0m || pivot.Lines.Any(line => line.Taxes.Any(tax => tax.TaxAmount != 0m));
}

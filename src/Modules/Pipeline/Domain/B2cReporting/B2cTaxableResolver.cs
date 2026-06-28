namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Résout la contribution d'UN document B2C TAXABLE au régime du prix total (enchères, commettant assujetti) —
/// cœur PUR, fail-closed (F03 §2.7). La base est le <b>prix total payé par l'adjudicataire</b> = adjudication
/// (HT + TVA SOURCÉS) + commission acheteur (TTC), sourcé BOI-TVA-SECT-90-50 §270 (la commission acheteur est un
/// frais accessoire à la livraison → même base TLB1, jamais une prestation distincte). Regroupe par TAUX, sans
/// recalculer les montants d'adjudication (HT/TVA sourcés) et sans convertir la commission ici (TTC porté tel
/// quel — la conversion HT est faite à l'agrégat du jour, <see cref="B2cTaxableAggregationCalculator"/>, cohérent
/// marge §2.5). <b>Bloque</b> (jamais ne devine, CLAUDE.md n°2/3) si aucune base, ou si un taux (adjudication ou
/// commission) n'est pas résolu par la table validée. La <b>commission vendeur</b> d'un lot taxable est HORS de
/// cette base (prestation B2B, F03 §2.7) — l'appelant ne fournit ici que la commission ACHETEUR.
/// </summary>
public static class B2cTaxableResolver
{
    /// <summary>Résout les composantes par taux d'un document taxable, ou le bloque (fail-closed).</summary>
    /// <param name="adjudicationLines">Lignes d'adjudication taxables (HT + TVA sourcés, taux mappé).</param>
    /// <param name="buyerHonoraires">Commission(s) ACHETEUR (TTC, taux mappé) — la commission vendeur est exclue (F03 §2.7).</param>
    /// <returns>La résolution (composantes par taux) ou un blocage typé.</returns>
    public static B2cTaxableResolution Resolve(
        IReadOnlyList<B2cTaxableLineAmount> adjudicationLines,
        IReadOnlyList<B2cResolvedHonoraire> buyerHonoraires)
    {
        ArgumentNullException.ThrowIfNull(adjudicationLines);
        ArgumentNullException.ThrowIfNull(buyerHonoraires);

        if (adjudicationLines.Count == 0 && buyerHonoraires.Count == 0)
        {
            return B2cTaxableResolution.Blocked(B2cTaxableBlockReason.NoTaxableBase);
        }

        // Tout taux doit être résolu par la table validée — jamais deviné (fail-closed, F03 §2.7 / §4.1).
        if (adjudicationLines.Any(l => l.RatePercent is null) || buyerHonoraires.Any(h => h.RatePercent is null))
        {
            return B2cTaxableResolution.Blocked(B2cTaxableBlockReason.UnmappedRate);
        }

        // Regroupement par taux : adjudication (HT/TVA sourcés, additionnés) + commission acheteur (TTC, additionné).
        var perRate = new Dictionary<decimal, (decimal AdjHt, decimal AdjVat, decimal HonoTtc)>();

        foreach (var line in adjudicationLines)
        {
            var rate = line.RatePercent!.Value;
            var acc = perRate.TryGetValue(rate, out var cur) ? cur : (AdjHt: 0m, AdjVat: 0m, HonoTtc: 0m);
            perRate[rate] = (acc.AdjHt + line.TaxableHt, acc.AdjVat + line.TaxVat, acc.HonoTtc);
        }

        foreach (var honoraire in buyerHonoraires)
        {
            var rate = honoraire.RatePercent!.Value;
            var acc = perRate.TryGetValue(rate, out var cur) ? cur : (AdjHt: 0m, AdjVat: 0m, HonoTtc: 0m);
            perRate[rate] = (acc.AdjHt, acc.AdjVat, acc.HonoTtc + honoraire.AmountTtc);
        }

        var components = perRate
            .OrderBy(kv => kv.Key)
            .Select(kv => new B2cTaxableRateComponent
            {
                RatePercent = kv.Key,
                AdjudicationHt = kv.Value.AdjHt,
                AdjudicationVat = kv.Value.AdjVat,
                HonoraireTtc = kv.Value.HonoTtc,
            })
            .ToList();

        return B2cTaxableResolution.Resolved(components);
    }
}

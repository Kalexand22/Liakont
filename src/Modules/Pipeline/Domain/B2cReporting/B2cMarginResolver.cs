namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System;
using System.Collections.Generic;
using System.Linq;
using Liakont.Agent.Contracts;

/// <summary>
/// Résout la contribution de marge d'UN document B2C-marge (enchères) — cœur PUR, fail-closed. La marge est
/// la <b>SOMME des honoraires acheteur + vendeur</b> (commission totale, F03 §2.4 / BOI-TVA-SECT-90-50 §270 —
/// JAMAIS de séparation acheteur/vendeur), à un taux <b>UNIQUE</b> (mapping F03 de l'honoraire). <b>Bloque</b>
/// (jamais ne devine, CLAUDE.md n°2/3) si : TVA distincte (art. 297 E), aucun honoraire, code TVA non mappé,
/// ou honoraires d'une même vente à taux mixtes (découpage non sourcé, F03 §2.5). La nature TTC est sommée
/// ici ; la conversion en HT est faite à l'agrégation (<see cref="B2cTransactionAggregationCalculator"/>).
/// </summary>
public static class B2cMarginResolver
{
    /// <summary>Résout la marge (TTC + taux) d'un document, ou la bloque (fail-closed).</summary>
    /// <param name="documentHasSeparateVat">Vrai si le document fait apparaître une TVA distincte (art. 297 E).</param>
    /// <param name="honoraires">Honoraires acheteur + vendeur, taux déjà résolu (mapping F03).</param>
    /// <returns>La contribution résolue, ou un blocage typé.</returns>
    public static B2cMarginResolution Resolve(
        bool documentHasSeparateVat,
        IReadOnlyList<B2cResolvedHonoraire> honoraires)
    {
        ArgumentNullException.ThrowIfNull(honoraires);

        if (documentHasSeparateVat)
        {
            return B2cMarginResolution.Blocked(B2cMarginBlockReason.SeparateVat);
        }

        if (honoraires.Count == 0)
        {
            return B2cMarginResolution.Blocked(B2cMarginBlockReason.NoHonoraires);
        }

        if (honoraires.Any(h => h.RatePercent is null))
        {
            return B2cMarginResolution.Blocked(B2cMarginBlockReason.UnmappedRate);
        }

        var distinctRates = honoraires.Select(h => h.RatePercent!.Value).Distinct().ToList();
        if (distinctRates.Count > 1)
        {
            return B2cMarginResolution.Blocked(B2cMarginBlockReason.MixedRates);
        }

        var marginTtc = PivotRounding.RoundAmount(honoraires.Sum(h => h.AmountTtc));
        return B2cMarginResolution.Resolved(marginTtc, distinctRates[0]);
    }
}

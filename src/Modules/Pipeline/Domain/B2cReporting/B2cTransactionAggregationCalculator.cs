namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System;
using System.Collections.Generic;
using System.Linq;
using Liakont.Agent.Contracts;

/// <summary>
/// Cœur PUR (sans I/O) de l'agrégation N→1 de l'e-reporting B2C (flux 10.3). À partir des contributions par
/// document (<see cref="B2cMarginContribution"/>, marge TTC à son taux), agrège par <b>jour × devise</b>
/// avec des sous-totaux <b>par taux</b> (F03 §2.5 — grain jour×devise×taux sourcé), et ramène chaque marge
/// TTC en HT : <c>HT = arrondi(TTC / (1 + taux))</c>, <c>TVA = TTC − HT</c> — arrondi commercial half-up
/// (<see cref="PivotRounding.RoundAmount"/>), <see cref="decimal"/> exclusivement (CLAUDE.md n°1).
/// Déterministe (ordre jour, devise, taux, référence source).
/// <para>
/// AUCUNE règle fiscale inventée (n°2) : la composition (somme acheteur + vendeur, F03 §2.4) et le passage
/// HT (F03 §2.5) sont ancrés ; le taux vient du mapping F03 (résolu en amont). La <b>séparation
/// acheteur/vendeur</b> et le <b>découpage d'une marge à taux mixtes</b> sont EXCLUS en amont (jamais ici).
/// Les sous-totaux par taux ne traduisent que des <b>ventes distinctes du jour</b> à des taux différents.
/// </para>
/// </summary>
public static class B2cTransactionAggregationCalculator
{
    /// <summary>Agrège les contributions par jour × devise (sous-totaux par taux). Collection vide → résultat vide.</summary>
    /// <param name="contributions">Contributions par document (marge TTC + taux résolu).</param>
    /// <returns>Les transactions agrégées, dans un ordre déterministe.</returns>
    public static IReadOnlyList<B2cAggregatedTransaction> Aggregate(IReadOnlyCollection<B2cMarginContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);

        var transactions = new List<B2cAggregatedTransaction>();

        foreach (var dayGroup in contributions
            .GroupBy(c => (c.Date, c.CurrencyCode))
            .OrderBy(g => g.Key.Date)
            .ThenBy(g => g.Key.CurrencyCode, StringComparer.Ordinal))
        {
            var subtotals = new List<B2cAggregatedSubtotal>();
            decimal totalHt = 0m;
            decimal totalVat = 0m;

            foreach (var rateGroup in dayGroup.GroupBy(c => c.RatePercent).OrderBy(g => g.Key))
            {
                var marginTtc = PivotRounding.RoundAmount(rateGroup.Sum(c => c.MarginTtc));
                var taxableHt = PivotRounding.RoundAmount(marginTtc / (1m + (rateGroup.Key / 100m)));

                // TVA = TTC − HT : la marge ramenée HT + sa TVA reconcilient le TTC exactement (jamais de dérive).
                var vat = marginTtc - taxableHt;

                subtotals.Add(new B2cAggregatedSubtotal
                {
                    RatePercent = rateGroup.Key,
                    TaxableAmount = taxableHt,
                    TaxTotal = vat,
                });
                totalHt += taxableHt;
                totalVat += vat;
            }

            var refs = dayGroup
                .OrderBy(c => c.SourceReference, StringComparer.Ordinal)
                .ThenBy(c => c.DocumentId)
                .Select(c => new B2cContributionRef { DocumentId = c.DocumentId, SourceReference = c.SourceReference })
                .ToList();

            transactions.Add(new B2cAggregatedTransaction
            {
                Date = dayGroup.Key.Date,
                CurrencyCode = dayGroup.Key.CurrencyCode,
                TaxExclusiveAmount = PivotRounding.RoundAmount(totalHt),
                TaxTotal = PivotRounding.RoundAmount(totalVat),
                Subtotals = subtotals,
                Contributions = refs,
            });
        }

        return transactions;
    }
}

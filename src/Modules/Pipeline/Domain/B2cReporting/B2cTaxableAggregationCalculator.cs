namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System;
using System.Collections.Generic;
using System.Linq;
using Liakont.Agent.Contracts;

/// <summary>
/// Cœur PUR (sans I/O) de l'agrégation N→1 de l'e-reporting B2C TAXABLE au régime du prix total (flux 10.3,
/// catégorie TLB1 — F03 §2.7). À partir des contributions par document (<see cref="B2cTaxableContribution"/>),
/// agrège par <b>jour × devise</b> avec des sous-totaux <b>par taux</b> (grain sourcé F03 §2.5). Pour chaque
/// taux : la part ADJUDICATION (HT + TVA SOURCÉS) est sommée TELLE QUELLE (jamais recalculée) ; la part
/// COMMISSION ACHETEUR (TTC) est sommée puis ramenée HT À L'AGRÉGAT — <c>HT = arrondi(ΣTTC / (1 + taux))</c>,
/// <c>TVA = ΣTTC − HT</c> (un seul point d'arrondi par taux/jour, cohérent marge §2.5, zéro dérive). La base et
/// la TVA du sous-total = adjudication (sourcée) + commission (convertie). Arrondi commercial half-up
/// (<see cref="PivotRounding.RoundAmount"/>), <see cref="decimal"/> exclusivement (CLAUDE.md n°1). Déterministe.
/// <para>AUCUNE règle fiscale inventée (n°2) : composition (prix total = adjudication + commission acheteur) et
/// conversion HT sont ancrées §2.7/§270/§2.5 ; les taux viennent du mapping F03 (résolus en amont, fail-closed).</para>
/// </summary>
public static class B2cTaxableAggregationCalculator
{
    /// <summary>Agrège les contributions par jour × devise (sous-totaux par taux). Collection vide → résultat vide.</summary>
    /// <param name="contributions">Contributions par document (adjudication HT/TVA sourcée + commission TTC, par taux).</param>
    /// <returns>Les transactions agrégées, dans un ordre déterministe.</returns>
    public static IReadOnlyList<B2cAggregatedTransaction> Aggregate(IReadOnlyCollection<B2cTaxableContribution> contributions)
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
                // Adjudication : HT/TVA SOURCÉS sommés tels quels (jamais recalculés).
                var adjHt = rateGroup.Sum(c => c.AdjudicationHt);
                var adjVat = rateGroup.Sum(c => c.AdjudicationVat);

                // Commission acheteur : ΣTTC ramenée HT À L'AGRÉGAT (un seul arrondi par taux/jour).
                var honoTtc = PivotRounding.RoundAmount(rateGroup.Sum(c => c.HonoraireTtc));
                var honoHt = PivotRounding.RoundAmount(honoTtc / (1m + (rateGroup.Key / 100m)));
                var honoVat = honoTtc - honoHt;

                var taxableHt = PivotRounding.RoundAmount(adjHt + honoHt);
                var vat = PivotRounding.RoundAmount(adjVat + honoVat);

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
                .Select(c => (c.DocumentId, c.SourceReference))
                .Distinct()
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

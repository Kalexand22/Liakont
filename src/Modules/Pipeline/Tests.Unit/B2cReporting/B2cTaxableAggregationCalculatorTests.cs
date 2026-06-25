namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Xunit;

/// <summary>
/// Couvre l'agrégation N→1 de l'e-reporting B2C taxable (<see cref="B2cTaxableAggregationCalculator"/>, F03 §2.7) :
/// adjudication (HT/TVA SOURCÉES sommées telles quelles) + commission acheteur (ΣTTC ramenée HT À L'AGRÉGAT),
/// par jour × devise × taux. Tout en <see cref="decimal"/> half-up.
/// </summary>
public sealed class B2cTaxableAggregationCalculatorTests
{
    private static readonly DateOnly Day = new(2024, 1, 12);

    [Fact]
    public void SingleContribution_SumsAdjudicationSourced_AndConvertsHonoraire()
    {
        // adj HT 2000 + TVA 400 (sourcés) ; commission 480 TTC @20% → HT 400, TVA 80. Sous-total HT 2400, TVA 480.
        var result = B2cTaxableAggregationCalculator.Aggregate(
        [
            Contribution(adjHt: 2000m, adjVat: 400m, honoTtc: 480m, rate: 20m),
        ]);

        result.Should().ContainSingle();
        var tx = result[0];
        tx.TaxExclusiveAmount.Should().Be(2400m);
        tx.TaxTotal.Should().Be(480m);
        tx.Subtotals.Should().ContainSingle();
        tx.Subtotals[0].RatePercent.Should().Be(20m);
        tx.Subtotals[0].TaxableAmount.Should().Be(2400m);
        tx.Subtotals[0].TaxTotal.Should().Be(480m);
    }

    [Fact]
    public void HonoraireConversion_HappensAtAggregate_NotPerDocument()
    {
        // 3 commissions de 0,50 TTC @20% : conversion à l'AGRÉGAT → HT = round(1,50/1,2) = 1,25 (et non 3×0,42 = 1,26).
        var result = B2cTaxableAggregationCalculator.Aggregate(
        [
            Contribution(0m, 0m, 0.50m, 20m, doc: 1),
            Contribution(0m, 0m, 0.50m, 20m, doc: 2),
            Contribution(0m, 0m, 0.50m, 20m, doc: 3),
        ]);

        result[0].Subtotals[0].TaxableAmount.Should().Be(1.25m);
        result[0].Subtotals[0].TaxTotal.Should().Be(0.25m);
    }

    [Fact]
    public void MultipleRates_ProduceOrderedSubtotals()
    {
        var result = B2cTaxableAggregationCalculator.Aggregate(
        [
            Contribution(2000m, 400m, 0m, 20m, doc: 1),
            Contribution(1000m, 55m, 0m, 5.5m, doc: 2),
        ]);

        result.Should().ContainSingle();
        result[0].Subtotals.Select(s => s.RatePercent).Should().Equal(5.5m, 20m);
        result[0].TaxExclusiveAmount.Should().Be(3000m);
        result[0].TaxTotal.Should().Be(455m);
    }

    [Fact]
    public void DifferentDays_ProduceDistinctTransactions()
    {
        var result = B2cTaxableAggregationCalculator.Aggregate(
        [
            new B2cTaxableContribution { DocumentId = Guid.NewGuid(), SourceReference = "encheresv6:ba:1", Date = new DateOnly(2024, 1, 12), CurrencyCode = "EUR", RatePercent = 20m, AdjudicationHt = 1000m, AdjudicationVat = 200m, HonoraireTtc = 0m },
            new B2cTaxableContribution { DocumentId = Guid.NewGuid(), SourceReference = "encheresv6:ba:2", Date = new DateOnly(2024, 1, 13), CurrencyCode = "EUR", RatePercent = 20m, AdjudicationHt = 500m, AdjudicationVat = 100m, HonoraireTtc = 0m },
        ]);

        result.Should().HaveCount(2);
        result.Select(t => t.Date).Should().Equal(new DateOnly(2024, 1, 12), new DateOnly(2024, 1, 13));
    }

    [Fact]
    public void ContributionRefs_AreDeduplicatedByDocument()
    {
        // Un même document à DEUX taux produit deux contributions → une seule référence dans l'agrégat.
        var docId = Guid.NewGuid();
        var result = B2cTaxableAggregationCalculator.Aggregate(
        [
            new B2cTaxableContribution { DocumentId = docId, SourceReference = "encheresv6:ba:7", Date = Day, CurrencyCode = "EUR", RatePercent = 20m, AdjudicationHt = 1000m, AdjudicationVat = 200m, HonoraireTtc = 0m },
            new B2cTaxableContribution { DocumentId = docId, SourceReference = "encheresv6:ba:7", Date = Day, CurrencyCode = "EUR", RatePercent = 5.5m, AdjudicationHt = 100m, AdjudicationVat = 5.5m, HonoraireTtc = 0m },
        ]);

        result[0].Contributions.Should().ContainSingle();
        result[0].Contributions[0].DocumentId.Should().Be(docId);
    }

    [Fact]
    public void Empty_ProducesEmpty()
    {
        B2cTaxableAggregationCalculator.Aggregate([]).Should().BeEmpty();
    }

    private static B2cTaxableContribution Contribution(decimal adjHt, decimal adjVat, decimal honoTtc, decimal rate, int doc = 0) =>
        new()
        {
            DocumentId = Guid.NewGuid(),
            SourceReference = $"encheresv6:ba:{doc}",
            Date = Day,
            CurrencyCode = "EUR",
            RatePercent = rate,
            AdjudicationHt = adjHt,
            AdjudicationVat = adjVat,
            HonoraireTtc = honoTtc,
        };
}

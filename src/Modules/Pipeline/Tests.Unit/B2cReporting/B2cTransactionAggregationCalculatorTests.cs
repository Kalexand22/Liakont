namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Xunit;

/// <summary>
/// Couvre le cœur PUR de l'agrégation N→1 e-reporting B2C (flux 10.3, F03 §2.5) : ramené HT (TTC/(1+taux)),
/// arrondi half-up decimal réconciliant le TTC (CLAUDE.md n°1), agrégation par jour × devise × taux,
/// réversibilité (contributions rattachées), déterminisme. PAS de séparation acheteur/vendeur (les deux
/// honoraires sont déjà sommés dans <see cref="B2cMarginContribution.MarginTtc"/>).
/// </summary>
public sealed class B2cTransactionAggregationCalculatorTests
{
    private static B2cMarginContribution Contribution(
        decimal marginTtc,
        decimal rate,
        DateOnly date,
        string sourceRef = "BA-1",
        string currency = "EUR")
        => new()
        {
            DocumentId = Guid.NewGuid(),
            SourceReference = sourceRef,
            Date = date,
            CurrencyCode = currency,
            MarginTtc = marginTtc,
            RatePercent = rate,
        };

    [Fact]
    public void SingleContribution_RamenesMargeHt_AndVat()
    {
        var d = new DateOnly(2026, 6, 22);
        var result = B2cTransactionAggregationCalculator.Aggregate([Contribution(120.00m, 20.0m, d)]);

        result.Should().ContainSingle();
        var tx = result[0];
        tx.Date.Should().Be(d);
        tx.Subtotals.Should().ContainSingle();
        tx.Subtotals[0].RatePercent.Should().Be(20.0m);
        tx.Subtotals[0].TaxableAmount.Should().Be(100.00m, "120 TTC / 1.2 = 100 HT");
        tx.Subtotals[0].TaxTotal.Should().Be(20.00m);
        tx.TaxExclusiveAmount.Should().Be(100.00m);
        tx.TaxTotal.Should().Be(20.00m);
    }

    [Fact]
    public void Rounding_HalfUp_TwoDecimals_AndReconcilesToTtc()
    {
        // 100.00 TTC à 20 % : HT = 100 / 1.2 = 83.3333… → half-up 83.33 ; TVA = 100.00 − 83.33 = 16.67.
        var result = B2cTransactionAggregationCalculator.Aggregate([Contribution(100.00m, 20.0m, new DateOnly(2026, 6, 22))]);

        var st = result[0].Subtotals[0];
        st.TaxableAmount.Should().Be(83.33m);
        st.TaxTotal.Should().Be(16.67m);
        (st.TaxableAmount + st.TaxTotal).Should().Be(100.00m, "HT + TVA reconcilient le TTC exactement (n°1)");
    }

    [Fact]
    public void MultipleDocs_SameDaySameRate_AreSummed_WithAllContributionsLinked()
    {
        var d = new DateOnly(2026, 6, 22);
        var result = B2cTransactionAggregationCalculator.Aggregate(
        [
            Contribution(120.00m, 20.0m, d, "BA-1"),
            Contribution(60.00m, 20.0m, d, "BV-1"),
        ]);

        result.Should().ContainSingle();
        result[0].Subtotals.Should().ContainSingle();
        result[0].Subtotals[0].TaxableAmount.Should().Be(150.00m, "180 TTC / 1.2 = 150 HT");
        result[0].Subtotals[0].TaxTotal.Should().Be(30.00m);
        result[0].Contributions.Should().HaveCount(2, "réversibilité N→1 : les deux pièces sont rattachées");
        result[0].Contributions.Select(c => c.SourceReference).Should().BeEquivalentTo(["BA-1", "BV-1"]);
    }

    [Fact]
    public void DifferentDays_ProduceSeparateTransactions_OrderedByDay()
    {
        var result = B2cTransactionAggregationCalculator.Aggregate(
        [
            Contribution(120.00m, 20.0m, new DateOnly(2026, 6, 23), "BA-2"),
            Contribution(120.00m, 20.0m, new DateOnly(2026, 6, 22), "BA-1"),
        ]);

        result.Should().HaveCount(2);
        result[0].Date.Should().Be(new DateOnly(2026, 6, 22), "ordre déterministe par jour");
        result[1].Date.Should().Be(new DateOnly(2026, 6, 23));
    }

    [Fact]
    public void DistinctSales_SameDayDifferentRates_ProduceMultipleSubtotals()
    {
        // Deux VENTES distinctes du jour à des taux différents → 2 sous-totaux (flux par taux, sourcé).
        // Ce n'est PAS une séparation acheteur/vendeur — chaque contribution est déjà la marge sommée d'une vente.
        var d = new DateOnly(2026, 6, 22);
        var result = B2cTransactionAggregationCalculator.Aggregate(
        [
            Contribution(120.00m, 20.0m, d, "BA-1"),
            Contribution(105.00m, 5.0m, d, "BA-2"),
        ]);

        result.Should().ContainSingle();
        result[0].Subtotals.Should().HaveCount(2);
        result[0].Subtotals.Single(s => s.RatePercent == 20.0m).TaxableAmount.Should().Be(100.00m);
        result[0].Subtotals.Single(s => s.RatePercent == 5.0m).TaxableAmount.Should().Be(100.00m, "105 / 1.05 = 100");
        result[0].TaxExclusiveAmount.Should().Be(200.00m);
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        B2cTransactionAggregationCalculator.Aggregate([]).Should().BeEmpty();
    }
}

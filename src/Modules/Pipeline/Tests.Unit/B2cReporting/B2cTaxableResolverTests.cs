namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Xunit;

/// <summary>
/// Couvre le cœur PUR de résolution de la base B2C taxable (<see cref="B2cTaxableResolver"/>, F03 §2.7) :
/// adjudication (HT/TVA SOURCÉES) + commission acheteur (TTC) regroupées par taux, fail-closed sur taux non
/// mappé / base vide. La commission acheteur reste TTC (la conversion HT est différée à l'agrégat).
/// </summary>
public sealed class B2cTaxableResolverTests
{
    [Fact]
    public void AdjudicationAndBuyerFee_SameRate_AreGroupedIntoOneComponent()
    {
        var resolution = B2cTaxableResolver.Resolve(
            adjudicationLines: [Adj(rate: 20m, ht: 2000m, vat: 400m)],
            buyerHonoraires: [Hono(ttc: 480m, rate: 20m)]);

        resolution.IsResolved.Should().BeTrue();
        resolution.Components.Should().ContainSingle();
        var c = resolution.Components![0];
        c.RatePercent.Should().Be(20m);
        c.AdjudicationHt.Should().Be(2000m);
        c.AdjudicationVat.Should().Be(400m);
        c.HonoraireTtc.Should().Be(480m); // TTC porté tel quel (conversion HT à l'agrégat).
    }

    [Fact]
    public void DistinctRates_ProduceDistinctComponents_OrderedByRate()
    {
        var resolution = B2cTaxableResolver.Resolve(
            adjudicationLines: [Adj(20m, 2000m, 400m), Adj(5.5m, 1000m, 55m)],
            buyerHonoraires: [Hono(120m, 20m)]);

        resolution.IsResolved.Should().BeTrue();
        resolution.Components!.Should().HaveCount(2);
        resolution.Components![0].RatePercent.Should().Be(5.5m);
        resolution.Components![1].RatePercent.Should().Be(20m);
        resolution.Components![1].HonoraireTtc.Should().Be(120m);
    }

    [Fact]
    public void AdjudicationOnly_NoBuyerFee_IsResolved()
    {
        var resolution = B2cTaxableResolver.Resolve([Adj(20m, 2000m, 400m)], []);

        resolution.IsResolved.Should().BeTrue();
        resolution.Components![0].HonoraireTtc.Should().Be(0m);
    }

    [Fact]
    public void Empty_IsBlocked_NoTaxableBase()
    {
        var resolution = B2cTaxableResolver.Resolve([], []);

        resolution.IsResolved.Should().BeFalse();
        resolution.BlockReason.Should().Be(B2cTaxableBlockReason.NoTaxableBase);
    }

    [Fact]
    public void UnmappedAdjudicationRate_IsBlocked()
    {
        var resolution = B2cTaxableResolver.Resolve([Adj(null, 2000m, 400m)], [Hono(480m, 20m)]);

        resolution.IsResolved.Should().BeFalse();
        resolution.BlockReason.Should().Be(B2cTaxableBlockReason.UnmappedRate);
    }

    [Fact]
    public void UnmappedHonoraireRate_IsBlocked()
    {
        var resolution = B2cTaxableResolver.Resolve([Adj(20m, 2000m, 400m)], [Hono(480m, null)]);

        resolution.IsResolved.Should().BeFalse();
        resolution.BlockReason.Should().Be(B2cTaxableBlockReason.UnmappedRate);
    }

    private static B2cTaxableLineAmount Adj(decimal? rate, decimal ht, decimal vat) =>
        new() { RatePercent = rate, TaxableHt = ht, TaxVat = vat };

    private static B2cResolvedHonoraire Hono(decimal ttc, decimal? rate) =>
        new() { AmountTtc = ttc, RatePercent = rate };
}

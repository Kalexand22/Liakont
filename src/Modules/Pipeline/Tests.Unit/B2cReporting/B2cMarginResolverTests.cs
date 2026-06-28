namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using FluentAssertions;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Xunit;

/// <summary>
/// Couvre la résolution PURE de la contribution de marge d'un document (F03 §2.4/§2.5) : somme acheteur +
/// vendeur (PAS de séparation), taux unique, et BLOCAGE fail-closed (297 E, code non mappé, taux mixtes).
/// </summary>
public sealed class B2cMarginResolverTests
{
    private static B2cResolvedHonoraire H(decimal amount, decimal? rate) => new() { AmountTtc = amount, RatePercent = rate };

    [Fact]
    public void TwoHonoraires_SameRate_AreSummed_AsTtc_NoSeparation()
    {
        var r = B2cMarginResolver.Resolve(false, [H(120.00m, 20.0m), H(60.00m, 20.0m)]);

        r.IsResolved.Should().BeTrue();
        r.MarginTtc.Should().Be(180.00m, "marge = somme acheteur + vendeur (TTC) — jamais séparées (§270)");
        r.RatePercent.Should().Be(20.0m);
        r.BlockReason.Should().BeNull();
    }

    [Fact]
    public void SeparateVat_IsBlocked_297E()
    {
        var r = B2cMarginResolver.Resolve(true, [H(120.00m, 20.0m)]);

        r.IsResolved.Should().BeFalse();
        r.BlockReason.Should().Be(B2cMarginBlockReason.SeparateVat);
    }

    [Fact]
    public void NoHonoraires_IsBlocked()
    {
        B2cMarginResolver.Resolve(false, []).BlockReason.Should().Be(B2cMarginBlockReason.NoHonoraires);
    }

    [Fact]
    public void UnmappedRate_IsBlocked_FailClosed()
    {
        // Un honoraire dont le code TVA n'est pas mappé (taux null) → bloqué, jamais un taux deviné (n°2/n°3).
        B2cMarginResolver.Resolve(false, [H(120.00m, 20.0m), H(60.00m, null)])
            .BlockReason.Should().Be(B2cMarginBlockReason.UnmappedRate);
    }

    [Fact]
    public void MixedRates_SameSale_IsBlocked_NoUnsourcedSplit()
    {
        // Honoraires d'une MÊME vente à taux différents : découpage non sourcé → bloqué (F03 §2.5).
        B2cMarginResolver.Resolve(false, [H(120.00m, 20.0m), H(60.00m, 5.0m)])
            .BlockReason.Should().Be(B2cMarginBlockReason.MixedRates);
    }
}

namespace Liakont.Host.Tests.Unit.Components;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Host.Components;
using Xunit;

// FIX205 (P2 review) : épingle les libellés FR des catégories TVA (transcrits de F03 §2.1) contre toute dérive
// silencieuse. Ces valeurs DOIVENT rester identiques à GetTvaMappingEditOptionsHandler.CategoryLabels (même
// source F03 §2.1) ; ce test golden fait échouer toute modification non intentionnelle.
public sealed class VatCategoryDisplayTests
{
    [Theory]
    [InlineData(VatCategory.S, "S — Taux normal")]
    [InlineData(VatCategory.AA, "AA — Taux réduit")]
    [InlineData(VatCategory.AAA, "AAA — Taux particulier (super-réduit)")]
    [InlineData(VatCategory.Z, "Z — Taux zéro (assujetti)")]
    [InlineData(VatCategory.E, "E — Exonéré (motif VATEX requis)")]
    [InlineData(VatCategory.AE, "AE — Autoliquidation")]
    [InlineData(VatCategory.G, "G — Export hors UE détaxé")]
    [InlineData(VatCategory.K, "K — Livraison/prestation intracommunautaire")]
    [InlineData(VatCategory.O, "O — Hors champ d'application de la TVA")]
    public void For_Renders_The_Sourced_French_Label(VatCategory category, string expected)
    {
        VatCategoryDisplay.For(category).Should().Be(expected);
    }

    [Fact]
    public void For_Null_Category_Renders_A_Placeholder()
    {
        // Catégorie non encore mappée (null côté contrat) → « — », jamais un libellé inventé.
        VatCategoryDisplay.For(null).Should().Be("—");
    }
}

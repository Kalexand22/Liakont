namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Host.Components;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Liakont.Modules.TvaMapping.Domain.Services;
using Xunit;

// Mention d'affichage explicite du régime de la marge (art. 297 A/E). Fonction PURE : on prouve qu'elle ne
// déclenche QUE sur la signature marge (catégorie E + VATEX-EU-F/I/J) et retourne null partout ailleurs —
// aucune règle fiscale inventée, libellés transcrits de F03 §2.2.
public sealed class MarginRegimeDisplayTests
{
    [Theory]
    [InlineData("VATEX-EU-F", "Régime de la marge – biens d'occasion")]
    [InlineData("VATEX-EU-I", "Régime de la marge – œuvres d'art")]
    [InlineData("VATEX-EU-J", "Régime de la marge – objets de collection et d'antiquité")]
    public void For_Returns_The_Explicit_Margin_Mention_For_Each_Margin_Vatex(string vatex, string expected)
    {
        MarginRegimeDisplay.For(VatCategory.E, vatex).Should().Be(expected);
    }

    [Fact]
    public void For_Returns_Null_When_Category_Is_Not_Exonerated()
    {
        // Même avec un VATEX de marge, une catégorie non-E n'est pas un régime de la marge (incohérent) → pas de mention.
        MarginRegimeDisplay.For(VatCategory.S, "VATEX-EU-J").Should().BeNull();
        MarginRegimeDisplay.For(null, "VATEX-EU-J").Should().BeNull();
    }

    [Fact]
    public void For_Recognizes_Exactly_The_Canonical_Margin_Vatex_Set()
    {
        // Verrou anti-divergence : la mention d'affichage doit reconnaître EXACTEMENT les codes VATEX que le
        // Domain (B2cMarginMarking, source canonique F03 §2.2) traite comme régime de la marge. Si un 4ᵉ code marge
        // est un jour ajouté côté Domain sans être ajouté ici, CE test casse au rouge — au lieu d'une cellule qui
        // retomberait silencieusement sur le sec « E — Exonéré » (le défaut bénin que la feature corrige).
        foreach (var code in B2cMarginMarking.MarginVatexCodes)
        {
            MarginRegimeDisplay.For(VatCategory.E, code).Should().NotBeNull($"{code} est un code de marge canonique");
        }

        var recognized = B2cMarginMarking.MarginVatexCodes.Count(code => MarginRegimeDisplay.For(VatCategory.E, code) is not null);
        recognized.Should().Be(B2cMarginMarking.MarginVatexCodes.Count, "aucun code de marge canonique ne doit manquer à l'affichage");
    }

    [Fact]
    public void For_Uses_The_Canonical_Nature_Label_From_VatexCatalog()
    {
        // Verrou anti-divergence des LIBELLÉS : la nature affichée (« biens d'occasion »…) doit rester cohérente
        // avec le libellé canonique de VatexCatalog (F03 §2.2). Si le libellé canonique évolue sans mise à jour
        // ici, CE test casse au rouge — au lieu d'afficher silencieusement un libellé périmé.
        foreach (var code in B2cMarginMarking.MarginVatexCodes)
        {
            var mention = MarginRegimeDisplay.For(VatCategory.E, code);

            // Nature canonique = libellé VatexCatalog sans le suffixe « (régime de la marge) ».
            var canonicalNature = VatexCatalog.All.Single(entry => entry.Code == code).Description.Split('(')[0].Trim();

            // Comparaison en minuscules invariantes (la nature affichée est en minuscules, le catalogue capitalisé +
            // ligature Œ/œ) : la casse n'est pas l'objet du verrou, seul le libellé l'est.
            mention!.ToLowerInvariant().Should().Contain(
                canonicalNature.ToLowerInvariant(),
                $"la nature affichée pour {code} doit refléter le libellé canonique VatexCatalog");
        }
    }

    [Theory]
    [InlineData("VATEX-EU-AE")] // autoliquidation
    [InlineData("VATEX-EU-IC")] // livraison intra-UE
    [InlineData("VATEX-EU-G")] // export hors UE
    [InlineData("VATEX-FR-FRANCHISE")]
    [InlineData(null)]
    public void For_Returns_Null_For_A_Non_Margin_Exemption(string? vatex)
    {
        MarginRegimeDisplay.For(VatCategory.E, vatex).Should().BeNull();
    }
}

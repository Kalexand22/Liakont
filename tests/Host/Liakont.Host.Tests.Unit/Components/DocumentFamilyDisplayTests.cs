namespace Liakont.Host.Tests.Unit.Components;

using FluentAssertions;
using Liakont.Host.Components;
using Xunit;

/// <summary>
/// Dérivation d'affichage de la FAMILLE de pièce (BUG-20) depuis le préfixe de la référence source. Fonction
/// pure : famille reconnue → libellé FR ; référence vide / mal formée / famille inconnue → <c>null</c> (jamais
/// devinée). Couvre le fail-safe (autre adaptateur, segment non reconnu) qui garantit qu'aucune famille fausse
/// n'est affichée.
/// </summary>
public sealed class DocumentFamilyDisplayTests
{
    [Theory]
    [InlineData("encheresv6:ba:9000004", "Bordereau acheteur")]
    [InlineData("encheresv6:bv:9000005", "Bordereau vendeur")]
    [InlineData("encheresv6:fc:100348", "Facture client")]
    [InlineData("encheresv6:nh:200", "Note d'honoraires")]
    [InlineData("ENCHERESV6:BA:1", "Bordereau acheteur")]
    public void For_Maps_The_Known_Family_Segment_To_Its_French_Label(string sourceReference, string expected)
    {
        DocumentFamilyDisplay.For(sourceReference).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("legacy/2026-001")]
    [InlineData("no_ba=4007")]
    [InlineData("encheresv6:remise:42")]
    [InlineData("encheresv6:zz:1")]
    [InlineData("encheresv6:ba")]
    public void For_Returns_Null_For_An_Unknown_Or_Malformed_Reference(string? sourceReference)
    {
        DocumentFamilyDisplay.For(sourceReference).Should().BeNull();
    }
}

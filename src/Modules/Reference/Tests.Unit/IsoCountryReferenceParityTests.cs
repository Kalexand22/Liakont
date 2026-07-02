namespace Liakont.Modules.Reference.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Reference.Infrastructure;
using Liakont.Modules.Validation.Domain.Identity;
using Xunit;

/// <summary>
/// Verrou anti-dérive (ADR-0038 §5, INV-REF-CTRY-03) : <see cref="IsoCountryReference"/> (module Reference,
/// validation de la CIBLE d'un alias à l'écriture) DOIT accepter EXACTEMENT les mêmes codes que la liste
/// canonique <see cref="CountryCodeValidator"/> (Validation.Domain, BT-55). Sinon un alias validé côté
/// Reference pourrait être rejeté par BT-55 en aval (ou l'inverse) → « validé mais bloqué ». La liste est
/// DUPLIQUÉE volontairement (frontière inter-modules : Reference ne référence pas Validation.Domain en
/// production, CLAUDE.md n°14) ; ce test — qui, LUI, référence les deux — interdit toute divergence.
/// </summary>
public sealed class IsoCountryReferenceParityTests
{
    [Fact]
    public void IsoCountryReference_matches_CountryCodeValidator_for_every_two_letter_combination()
    {
        for (var a = 'A'; a <= 'Z'; a++)
        {
            for (var b = 'A'; b <= 'Z'; b++)
            {
                var code = $"{a}{b}";
                IsoCountryReference.IsValid(code).Should().Be(
                    CountryCodeValidator.IsValid(code),
                    "les deux listes ISO 3166-1 alpha-2 doivent être identiques (code {0})",
                    code);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("F")]
    [InlineData("FRA")]
    [InlineData("fr")]
    [InlineData(" FR")]
    [InlineData("XX")]
    public void IsoCountryReference_matches_CountryCodeValidator_on_edge_cases(string? code)
    {
        IsoCountryReference.IsValid(code).Should().Be(CountryCodeValidator.IsValid(code));
    }
}

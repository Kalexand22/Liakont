namespace Liakont.Modules.Validation.Tests.Unit.Identity;

using FluentAssertions;
using Liakont.Modules.Validation.Domain.Identity;
using Xunit;

public sealed class FrenchVatNumberValidatorTests
{
    [Theory]
    [InlineData("FR11123456782")] // clé 11 = (12 + 3 × (123456782 mod 97)) mod 97
    [InlineData("FR12000000000")] // clé 12 = (12 + 0) mod 97
    public void IsValid_with_correct_key_returns_true(string vatNumber)
    {
        FrenchVatNumberValidator.IsValid(vatNumber).Should().BeTrue("la clé est cohérente avec le SIREN (F04 §4.2).");
    }

    [Theory]
    [InlineData("FR12123456782")] // clé attendue 11, fournie 12
    [InlineData("FR99000000000")] // clé attendue 12, fournie 99
    public void IsValid_with_wrong_key_returns_false(string vatNumber)
    {
        FrenchVatNumberValidator.IsValid(vatNumber).Should().BeFalse("la clé ne correspond pas à la formule (F04 §4.2).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DE11123456782")] // mauvais préfixe pays
    [InlineData("FR1112345678")] // trop court (12 caractères)
    [InlineData("FR111234567820")] // trop long (14 caractères)
    [InlineData("FRAA123456782")] // clé non numérique
    [InlineData("FR1112345678X")] // SIREN non numérique
    public void IsValid_with_wrong_shape_returns_false(string? vatNumber)
    {
        FrenchVatNumberValidator.IsValid(vatNumber).Should().BeFalse("le format attendu est « FR » + clé (2 chiffres) + SIREN (9 chiffres).");
    }
}

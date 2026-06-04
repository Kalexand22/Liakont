namespace Liakont.Modules.Validation.Tests.Unit.Identity;

using FluentAssertions;
using Liakont.Modules.Validation.Domain.Identity;
using Xunit;

public sealed class SiretValidatorTests
{
    [Theory]
    [InlineData("12345678200002")] // SIRET fictif, clé de Luhn 14 chiffres valide
    [InlineData("00000000000000")]
    public void IsValid_with_luhn_valid_siret_returns_true(string siret)
    {
        SiretValidator.IsValid(siret).Should().BeTrue("le SIRET respecte la clé de Luhn sur 14 chiffres (F04 §4.1).");
    }

    [Theory]
    [InlineData("12345678200001")] // clé de Luhn invalide
    [InlineData("12345678212345")]
    public void IsValid_with_luhn_invalid_siret_returns_false(string siret)
    {
        SiretValidator.IsValid(siret).Should().BeFalse("la clé de Luhn n'est pas satisfaite (F04 §4.1).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567820000")] // 13 chiffres : trop court
    [InlineData("123456782000020")] // 15 chiffres : trop long
    [InlineData("1234567820000A")] // non numérique
    public void IsValid_with_wrong_shape_returns_false(string? siret)
    {
        SiretValidator.IsValid(siret).Should().BeFalse("un SIRET doit comporter exactement 14 chiffres.");
    }
}

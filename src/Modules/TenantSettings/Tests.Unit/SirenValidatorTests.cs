namespace Liakont.Modules.TenantSettings.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Domain.Services;
using Xunit;

public sealed class SirenValidatorTests
{
    [Theory]
    [InlineData("123456782")] // SIREN fictif, clé de Luhn valide
    [InlineData("000000000")]
    public void IsValid_With_Luhn_Valid_Siren_Returns_True(string siren)
    {
        SirenValidator.IsValid(siren).Should().BeTrue("le SIREN respecte la clé de Luhn (INV-TENANTSETTINGS-001).");
    }

    [Theory]
    [InlineData("123456789")] // clé de Luhn invalide
    [InlineData("111111111")]
    public void IsValid_With_Luhn_Invalid_Siren_Returns_False(string siren)
    {
        SirenValidator.IsValid(siren).Should().BeFalse("la clé de Luhn n'est pas satisfaite (INV-TENANTSETTINGS-001).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345678")] // trop court
    [InlineData("1234567890")] // trop long
    [InlineData("12345678A")] // non numérique
    public void IsValid_With_Wrong_Shape_Returns_False(string? siren)
    {
        SirenValidator.IsValid(siren).Should().BeFalse("un SIREN doit comporter exactement 9 chiffres.");
    }
}

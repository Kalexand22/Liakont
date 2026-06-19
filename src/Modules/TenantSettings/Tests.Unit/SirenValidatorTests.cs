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
    [InlineData("123456789")] // 9 chiffres, clé de Luhn NON satisfaite
    [InlineData("111111111")]
    [InlineData("000000002")] // SIREN de test sandbox SuperPDP (« Burger Queen »), Luhn non satisfaite
    public void IsValid_Accepts_9Digit_Siren_Even_When_Luhn_Invalid(string siren)
    {
        // Décision de recette (Karl, 18/06/2026) : le SIREN du PROFIL TENANT n'est plus contrôlé par la clé
        // de Luhn (paramétrage de confiance ; autorise les SIREN de test des sandboxes PA). Seul le format
        // (9 chiffres) est exigé. La clé de Luhn reste appliquée aux SIREN extraits (Validation.SirenValidator).
        SirenValidator.IsValid(siren).Should().BeTrue("le SIREN du profil tenant n'impose plus la clé de Luhn (9 chiffres suffisent).");
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

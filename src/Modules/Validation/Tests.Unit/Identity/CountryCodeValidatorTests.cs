namespace Liakont.Modules.Validation.Tests.Unit.Identity;

using FluentAssertions;
using Liakont.Modules.Validation.Domain.Identity;
using Xunit;

public sealed class CountryCodeValidatorTests
{
    [Theory]
    [InlineData("FR")]
    [InlineData("DE")]
    [InlineData("US")]
    [InlineData("GB")]
    [InlineData("AQ")] // territoire officiel sans population permanente
    [InlineData("fr")] // insensible à la casse
    public void IsValid_with_assigned_iso_code_returns_true(string countryCode)
    {
        CountryCodeValidator.IsValid(countryCode).Should().BeTrue("le code appartient à ISO 3166-1 alpha-2 (F04 §3.2).");
    }

    [Theory]
    [InlineData("XK")] // user-assigned (Kosovo), pas officiellement ISO 3166-1
    [InlineData("ZZ")] // non assigné
    [InlineData("QZ")] // non assigné
    public void IsValid_with_unassigned_code_returns_false(string countryCode)
    {
        CountryCodeValidator.IsValid(countryCode).Should().BeFalse("le code n'est pas un alpha-2 officiellement assigné (F04 §3.2).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("F")] // trop court
    [InlineData("FRA")] // alpha-3, pas alpha-2
    [InlineData("F1")] // non alphabétique
    public void IsValid_with_wrong_shape_returns_false(string? countryCode)
    {
        CountryCodeValidator.IsValid(countryCode).Should().BeFalse("un code pays ISO 3166-1 alpha-2 fait exactement 2 lettres.");
    }
}

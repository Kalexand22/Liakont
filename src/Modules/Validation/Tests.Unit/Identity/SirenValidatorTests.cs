namespace Liakont.Modules.Validation.Tests.Unit.Identity;

using FluentAssertions;
using Liakont.Modules.Validation.Domain.Identity;
using Xunit;

public sealed class SirenValidatorTests
{
    [Theory]
    [InlineData("123456782")] // SIREN fictif, clé de Luhn valide
    [InlineData("000000000")]
    public void IsValid_with_luhn_valid_siren_returns_true(string siren)
    {
        SirenValidator.IsValid(siren).Should().BeTrue("le SIREN respecte la clé de Luhn (F04 §4.1).");
    }

    [Theory]
    [InlineData("123456789")] // clé de Luhn invalide
    [InlineData("111111111")]
    public void IsValid_with_luhn_invalid_siren_returns_false(string siren)
    {
        SirenValidator.IsValid(siren).Should().BeFalse("la clé de Luhn n'est pas satisfaite (F04 §4.1).");
    }

    [Fact]
    public void IsValid_with_la_poste_siren_returns_true()
    {
        // Dérogation documentée F04 §4.1 : le SIREN de La Poste est explicitement autorisé.
        SirenValidator.IsValid(SirenValidator.LaPosteSiren).Should().BeTrue("La Poste est autorisée par dérogation (F04 §4.1).");
    }

    [Theory]
    [InlineData("000000001")] // SuperPDP « Tricatel » (destinataire B2B adressable sandbox) — Luhn invalide
    [InlineData("000000002")] // SuperPDP « Burger Queen » (émetteur sandbox) — Luhn invalide
    public void IsValid_with_pa_sandbox_test_siren_returns_true(string siren)
    {
        // Dérogation de recette fermée (Karl, 27/06/2026) : les SIREN de test sandbox PA sont autorisés
        // bien qu'ils ne satisfassent PAS la clé de Luhn (exercer le pipeline e-invoicing B2B en recette).
        SirenValidator.IsValid(siren).Should().BeTrue("les SIREN de test sandbox PA sont autorisés par dérogation fermée.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345678")] // trop court
    [InlineData("1234567890")] // trop long
    [InlineData("12345678A")] // non numérique
    public void IsValid_with_wrong_shape_returns_false(string? siren)
    {
        SirenValidator.IsValid(siren).Should().BeFalse("un SIREN doit comporter exactement 9 chiffres.");
    }
}

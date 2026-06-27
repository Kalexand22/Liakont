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
    public void IsValid_with_pa_sandbox_test_siren_is_gated_by_environment(string siren)
    {
        // BUG-23 : la dérogation de recette (Karl, 27/06/2026) pour les SIREN de test sandbox PA est GÂTÉE par
        // l'environnement PA. Hors production (allowSandboxTestSirens=true), ces SIREN sont acceptés pour exercer
        // le pipeline e-invoicing B2B en recette ; en production (défaut strict), la clé de Luhn s'applique — ces
        // faux SIREN sont REFUSÉS (jamais d'affaiblissement silencieux d'une validation Blocking, CLAUDE.md n°3).
        SirenValidator.IsValid(siren, allowSandboxTestSirens: true).Should().BeTrue("hors production, les SIREN de test sandbox PA sont tolérés (recette).");
        SirenValidator.IsValid(siren).Should().BeFalse("en production (défaut strict), un SIREN de test sandbox échoue la clé de Luhn.");
        SirenValidator.IsValid(siren, allowSandboxTestSirens: false).Should().BeFalse("gating explicite : sans autorisation, la clé de Luhn s'applique.");
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

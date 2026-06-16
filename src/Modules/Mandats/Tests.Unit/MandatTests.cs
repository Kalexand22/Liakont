namespace Liakont.Modules.Mandats.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Mandats.Domain.Entities;
using Xunit;

/// <summary>
/// Agrégat <see cref="Mandat"/> (cycle de vie, F15 §1.5/§2.2, gabarit MappingTable) : naissance « NON
/// VALIDÉE », suspendu-par-défaut (INV-MANDATS-4), invalidation à chaque mutation (INV-MANDATS-6),
/// validation, révocation.
/// </summary>
public sealed class MandatTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromDays(30);

    private static Mandat NewMandat(
        bool estEcrit = true,
        string? assujettissementStatus = "ASSUJETTI",
        TimeSpan? contestationDelay = null)
    {
        contestationDelay ??= Delay;
        return Mandat.Create(Guid.NewGuid(), Guid.NewGuid(), "MDT-EXEMPLE-1", "Clause d'exemple", estEcrit, assujettissementStatus, contestationDelay);
    }

    [Fact]
    public void Create_Is_NonValidated_And_Suspended_By_Default()
    {
        var mandat = NewMandat();
        mandat.IsValidated.Should().BeFalse("un mandat naît « NON VALIDÉE ».");
        mandat.IsRevoked.Should().BeFalse();
        mandat.IsSelfBillingSuspended.Should().BeTrue("non validé ⇒ 389 suspendu (INV-MANDATS-4).");
    }

    [Theory]
    [InlineData("", "Clause")]
    [InlineData("MDT-1", "")]
    public void Create_Rejects_Missing_Required_Field(string reference, string clause)
    {
        var act = () => Mandat.Create(Guid.NewGuid(), Guid.NewGuid(), reference, clause, true, "ASSUJETTI", Delay);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Rejects_NonPositive_ContestationDelay()
    {
        var act = () => Mandat.Create(Guid.NewGuid(), Guid.NewGuid(), "MDT-1", "Clause", true, "ASSUJETTI", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>("null = suspendu, jamais un délai nul inventé.");
    }

    [Fact]
    public void Create_Allows_Null_Status_And_Null_Delay()
    {
        var mandat = Mandat.Create(Guid.NewGuid(), Guid.NewGuid(), "MDT-1", "Clause", false, null, null);
        mandat.AssujettissementStatus.Should().BeNull();
        mandat.ContestationDelay.Should().BeNull();
        mandat.IsSelfBillingSuspended.Should().BeTrue();
    }

    // Produit cartésien de INV-MANDATS-4 : 389 actif UNIQUEMENT si statut renseigné ET délai renseigné
    // ET validé ET non révoqué ; suspendu dès qu'une condition manque.
    [Theory]
    [InlineData(true, true, true, false, false)] // tout réuni → actif
    [InlineData(false, true, true, false, true)] // statut null → suspendu
    [InlineData(true, false, true, false, true)] // délai null → suspendu
    [InlineData(true, true, false, false, true)] // non validé → suspendu
    [InlineData(true, true, true, true, true)] // révoqué → suspendu
    public void IsSelfBillingSuspended_Honours_All_Four_Conditions(
        bool hasStatus, bool hasDelay, bool validated, bool revoked, bool expectedSuspended)
    {
        var mandat = Mandat.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "MDT-1",
            "Clause",
            estEcrit: true,
            assujettissementStatus: hasStatus ? "ASSUJETTI" : null,
            contestationDelay: hasDelay ? Delay : null);

        if (validated)
        {
            mandat.Validate("Valideur");
        }

        if (revoked)
        {
            mandat.Revoke();
        }

        mandat.IsSelfBillingSuspended.Should().Be(expectedSuspended);
    }

    [Fact]
    public void Validate_Then_UpdateTerms_Invalidates()
    {
        var mandat = NewMandat();
        mandat.Validate("Valideur");
        mandat.IsValidated.Should().BeTrue();
        mandat.IsSelfBillingSuspended.Should().BeFalse();

        mandat.UpdateTerms("Nouvelle clause", estEcrit: false, assujettissementStatus: "ASSUJETTI", contestationDelay: Delay);

        mandat.IsValidated.Should().BeFalse("toute mutation repasse le mandat « NON VALIDÉE » (INV-MANDATS-6).");
        mandat.IsSelfBillingSuspended.Should().BeTrue();
        mandat.ClauseText.Should().Be("Nouvelle clause");
        mandat.EstEcrit.Should().BeFalse();
    }

    [Fact]
    public void Validate_Requires_Validator_Identity()
    {
        var mandat = NewMandat();
        var act = () => mandat.Validate("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Revoke_Suspends_And_Is_Not_Idempotent()
    {
        var mandat = NewMandat();
        mandat.Validate("Valideur");

        mandat.Revoke();
        mandat.IsRevoked.Should().BeTrue();
        mandat.IsSelfBillingSuspended.Should().BeTrue("un mandat révoqué a 389 suspendu.");

        var act = () => mandat.Revoke();
        act.Should().Throw<InvalidOperationException>("révoquer un mandat déjà révoqué est une erreur, jamais un no-op.");
    }

    [Fact]
    public void UpdateTerms_On_Revoked_Mandat_Is_Rejected()
    {
        var mandat = NewMandat();
        mandat.Revoke();
        var act = () => mandat.UpdateTerms("Clause", true, "ASSUJETTI", Delay);
        act.Should().Throw<InvalidOperationException>();
    }
}

namespace Liakont.Modules.TenantSettings.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.TenantSettings.Domain.ValueObjects;
using Xunit;

/// <summary>
/// <see cref="TenantProfile.SetAlertContactEmail"/> (FIX210) : met à jour le SEUL contact d'alerte (F12 §5.3),
/// normalise le vide en <c>null</c>, valide le format, sans toucher au reste du profil.
/// </summary>
public sealed class TenantProfileAlertContactTests
{
    private static TenantProfile Profile() =>
        TenantProfile.Create(
            Guid.NewGuid(),
            "123456782",
            "Société Fictive",
            TenantAddress.Create("1 rue de l'Exemple", "35000", "Rennes", "FR"),
            contactEmailAlerte: null);

    [Fact]
    public void Sets_The_Contact_Email_And_Stamps_UpdatedAt()
    {
        var profile = Profile();

        profile.SetAlertContactEmail("alertes@exemple.test");

        profile.ContactEmailAlerte.Should().Be("alertes@exemple.test");
        profile.UpdatedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_Email_Is_Normalised_To_Null(string? blank)
    {
        var profile = Profile();
        profile.SetAlertContactEmail("alertes@exemple.test");

        profile.SetAlertContactEmail(blank);

        profile.ContactEmailAlerte.Should().BeNull();
    }

    [Fact]
    public void Invalid_Email_Throws_And_Leaves_The_Profile_Unchanged()
    {
        var profile = Profile();

        var act = () => profile.SetAlertContactEmail("pas-un-email");

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-002*");
        profile.ContactEmailAlerte.Should().BeNull();
    }

    [Fact]
    public void Does_Not_Alter_Other_Profile_Fields()
    {
        var profile = Profile();

        profile.SetAlertContactEmail("alertes@exemple.test");

        profile.Siren.Should().Be("123456782");
        profile.RaisonSociale.Should().Be("Société Fictive");
        profile.Statut.Should().Be(TenantStatus.Actif);
    }
}

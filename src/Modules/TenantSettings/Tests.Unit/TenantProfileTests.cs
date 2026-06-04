namespace Liakont.Modules.TenantSettings.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.TenantSettings.Domain.ValueObjects;
using Xunit;

public sealed class TenantProfileTests
{
    private static TenantAddress ValidAddress()
    {
        return TenantAddress.Create("1 rue de l'Exemple", "35000", "Rennes", "FR");
    }

    [Fact]
    public void Create_Valid_Sets_Status_Actif()
    {
        var profile = TenantProfile.Create(Guid.NewGuid(), "123456782", "Société Fictive", ValidAddress(), null);

        profile.Statut.Should().Be(TenantStatus.Actif);
        profile.UpdatedAt.Should().BeNull();
        profile.Siren.Should().Be("123456782");
    }

    [Fact]
    public void Create_With_Invalid_Siren_Throws()
    {
        var act = () => TenantProfile.Create(Guid.NewGuid(), "123456789", "Société Fictive", ValidAddress(), null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-001*");
    }

    [Fact]
    public void Create_With_Empty_RaisonSociale_Throws()
    {
        var act = () => TenantProfile.Create(Guid.NewGuid(), "123456782", "   ", ValidAddress(), null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-002*");
    }

    [Fact]
    public void Address_Create_With_Non_Iso_Country_Throws()
    {
        var act = () => TenantAddress.Create("1 rue", "35000", "Rennes", "France");

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-002*");
    }

    [Fact]
    public void Create_With_Invalid_Email_Throws()
    {
        var act = () => TenantProfile.Create(Guid.NewGuid(), "123456782", "Société Fictive", ValidAddress(), "pas-un-email");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Suspend_Then_Reactivate_Toggles_Status_And_Sets_UpdatedAt()
    {
        var profile = TenantProfile.Create(Guid.NewGuid(), "123456782", "Société Fictive", ValidAddress(), null);

        profile.Suspend();
        profile.Statut.Should().Be(TenantStatus.Suspendu);
        profile.UpdatedAt.Should().NotBeNull();

        profile.Reactivate();
        profile.Statut.Should().Be(TenantStatus.Actif);
    }
}

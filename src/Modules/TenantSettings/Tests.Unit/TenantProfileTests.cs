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

    [Theory]
    [InlineData("12345678")] // 8 chiffres : longueur != 9
    [InlineData("1234567890")] // 10 chiffres : longueur != 9
    [InlineData("12345678A")] // 9 caractères mais non numérique
    public void Create_With_Invalid_Siren_Throws(string invalidSiren)
    {
        // Le SIREN du PROFIL TENANT est un paramétrage de CONFIANCE : la règle est « 9 chiffres » (la clé de
        // Luhn n'est PAS imposée — décision de recette 18/06/2026, autorise les SIREN de test sandbox comme
        // « Burger Queen » 000000002, cf. SirenValidator). Un SIREN de longueur ≠ 9 ou non numérique reste
        // rejeté (INV-TENANTSETTINGS-001) ; un SIREN « 9 chiffres » sans clé de Luhn valide est ACCEPTÉ.
        var act = () => TenantProfile.Create(Guid.NewGuid(), invalidSiren, "Société Fictive", ValidAddress(), null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-001*");
    }

    [Fact]
    public void Create_With_Nine_Digit_Siren_Without_Valid_Luhn_Key_Is_Accepted()
    {
        // Garde-fou anti-régression (recette Bucodi, 18/06/2026) : un SIREN de 9 chiffres dont la clé de Luhn
        // n'est pas valide (ex. « 123456789 ») doit être ACCEPTÉ pour le profil tenant — sinon les SIREN de
        // test sandbox des PA seraient refusés au déploiement. La clé de Luhn n'est exigée que sur les SIREN
        // EXTRAITS (Validation.Domain.Identity.SirenValidator, VAL02), jamais sur ce paramétrage de confiance.
        var profile = TenantProfile.Create(Guid.NewGuid(), "123456789", "Société Fictive", ValidAddress(), null);

        profile.Siren.Should().Be("123456789");
        profile.Statut.Should().Be(TenantStatus.Actif);
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

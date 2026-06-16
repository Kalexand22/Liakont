namespace Liakont.Modules.Mandats.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Mandats.Domain.Entities;
using Xunit;

/// <summary>
/// Agrégat <see cref="Mandant"/> (registre, F15 §2.2) : construction, gardes de champ obligatoire
/// (aucune valeur par défaut inventée — INV-MANDATS-5), mise à jour en place.
/// </summary>
public sealed class MandantTests
{
    [Fact]
    public void Create_Trims_And_Keeps_All_Fields()
    {
        var companyId = Guid.NewGuid();
        var mandant = Mandant.Create(companyId, " M-EXEMPLE-1 ", " Ferme Exemple ", " FR00 000000000 ", " 000000000 ", " EXM- ");

        mandant.CompanyId.Should().Be(companyId);
        mandant.Reference.Should().Be("M-EXEMPLE-1");
        mandant.RaisonSociale.Should().Be("Ferme Exemple");
        mandant.SellerVatNumber.Should().Be("FR00 000000000");
        mandant.Siren.Should().Be("000000000");
        mandant.NumberingPrefix.Should().Be("EXM-");
        mandant.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Create_Allows_Null_SellerVatNumber()
    {
        var mandant = Mandant.Create(Guid.NewGuid(), "M-EXEMPLE-1", "Ferme Exemple", null, "000000000", "EXM-");
        mandant.SellerVatNumber.Should().BeNull();
    }

    [Fact]
    public void Create_Normalizes_Blank_SellerVatNumber_To_Null()
    {
        var mandant = Mandant.Create(Guid.NewGuid(), "M-EXEMPLE-1", "Ferme Exemple", "   ", "000000000", "EXM-");
        mandant.SellerVatNumber.Should().BeNull();
    }

    [Theory]
    [InlineData("", "Ferme", "000000000", "EXM-")]
    [InlineData("M-1", "", "000000000", "EXM-")]
    [InlineData("M-1", "Ferme", "", "EXM-")]
    [InlineData("M-1", "Ferme", "000000000", "")]
    public void Create_Rejects_Missing_Required_Field(string reference, string raisonSociale, string siren, string prefix)
    {
        var act = () => Mandant.Create(Guid.NewGuid(), reference, raisonSociale, null, siren, prefix);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateDetails_Changes_Registry_Fields_And_Stamps_UpdatedAt()
    {
        var mandant = Mandant.Create(Guid.NewGuid(), "M-EXEMPLE-1", "Ferme Exemple", null, "000000000", "EXM-");

        mandant.UpdateDetails("Ferme Exemple 2", "FR00 111111111", "111111111", "EX2-");

        mandant.RaisonSociale.Should().Be("Ferme Exemple 2");
        mandant.SellerVatNumber.Should().Be("FR00 111111111");
        mandant.Siren.Should().Be("111111111");
        mandant.NumberingPrefix.Should().Be("EX2-");
        mandant.Reference.Should().Be("M-EXEMPLE-1", "la clé métier n'est jamais changée par UpdateDetails.");
        mandant.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateDetails_Rejects_Missing_Required_Field()
    {
        var mandant = Mandant.Create(Guid.NewGuid(), "M-EXEMPLE-1", "Ferme Exemple", null, "000000000", "EXM-");
        var act = () => mandant.UpdateDetails(string.Empty, null, "000000000", "EXM-");
        act.Should().Throw<ArgumentException>();
    }
}

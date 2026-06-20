namespace Liakont.Modules.FacturX.Tests.Unit;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.FacturX.Infrastructure;
using Microsoft.Extensions.Configuration;
using QuestPDF.Infrastructure;
using Xunit;

/// <summary>
/// Résolution de la licence QuestPDF par configuration (RDF18, DEC-3). La licence n'est plus codée en
/// dur : <c>QuestPdf:LicenseType</c> est déclarée par topologie/instance et VALIDÉE au déploiement
/// (fail-closed). Seuls les noms d'énumération QuestPDF (Community / Professional / Enterprise) sont
/// admis ; toute valeur absente, vide, numérique ou inconnue échoue le démarrage avec un message
/// opérateur français citant la clé et les valeurs admises.
/// </summary>
public sealed class QuestPdfLicenseConfigurationTests
{
    [Theory]
    [InlineData("Community", LicenseType.Community)]
    [InlineData("Professional", LicenseType.Professional)]
    [InlineData("Enterprise", LicenseType.Enterprise)]
    [InlineData("community", LicenseType.Community)] // insensible à la casse
    [InlineData("  Enterprise  ", LicenseType.Enterprise)] // espaces tolérés
    public void Resolve_ReturnsDeclaredLicenseType(string configured, LicenseType expected)
    {
        QuestPdfLicenseConfiguration.Resolve(configured).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_Throws_WhenAbsentOrEmpty(string? configured)
    {
        var act = () => QuestPdfLicenseConfiguration.Resolve(configured);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*QuestPdf:LicenseType*")
            .WithMessage("*Community, Professional, Enterprise*");
    }

    [Theory]
    [InlineData("Premium")] // type inconnu
    [InlineData("Community-XL")] // proche mais invalide
    [InlineData("0")] // numérique : pas une déclaration explicite de type
    [InlineData("1")]
    [InlineData("Community,Enterprise")] // liste virgule : Enum.TryParse l'accepterait mais pas un nom exact
    public void Resolve_Throws_WhenUnknownOrNumeric(string configured)
    {
        var act = () => QuestPdfLicenseConfiguration.Resolve(configured);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*QuestPdf:LicenseType*");
    }

    [Fact]
    public void Resolve_ReadsFromConfigurationKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [QuestPdfLicenseConfiguration.LicenseTypeKey] = "Enterprise",
            })
            .Build();

        QuestPdfLicenseConfiguration.Resolve(configuration).Should().Be(LicenseType.Enterprise);
    }

    [Fact]
    public void Resolve_Throws_WhenConfigurationSectionMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => QuestPdfLicenseConfiguration.Resolve(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*QuestPdf:LicenseType*");
    }
}

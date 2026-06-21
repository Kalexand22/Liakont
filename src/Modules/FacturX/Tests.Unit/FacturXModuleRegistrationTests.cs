namespace Liakont.Modules.FacturX.Tests.Unit;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.FacturX.Application;
using Liakont.Modules.FacturX.Application.Cii;
using Liakont.Modules.FacturX.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using Xunit;

/// <summary>
/// Comportement de l'enregistrement DI du module FacturX (FX02 ; RDF18) : <c>AddFacturXModule</c> est
/// l'API de composition publique du module, à effet de bord (activation de la licence QuestPDF déclarée
/// par configuration — <c>QuestPdf:LicenseType</c>, plus de licence codée en dur). Elle chaîne, applique
/// le type de licence configuré et échoue le démarrage si la déclaration est absente (contrôle au
/// déploiement, fail-closed).
/// </summary>
[Collection(QuestPdfLicenseCollectionFixture.Name)]
public sealed class FacturXModuleRegistrationTests
{
    private static IConfiguration ConfigWith(string? licenseType)
    {
        var values = new Dictionary<string, string?>();
        if (licenseType is not null)
        {
            values[QuestPdfLicenseConfiguration.LicenseTypeKey] = licenseType;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void AddFacturXModule_ReturnsSameCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddFacturXModule(ConfigWith("Community"));

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void AddFacturXModule_AppliesConfiguredQuestPdfLicense()
    {
        new ServiceCollection().AddFacturXModule(ConfigWith("Professional"));

        QuestPDF.Settings.License.Should().Be(LicenseType.Professional);
    }

    [Fact]
    public void AddFacturXModule_FailsClosed_WhenLicenseTypeIsNotDeclared()
    {
        // Contrôle au déploiement (RDF18) : une licence QuestPDF non déclarée échoue le démarrage plutôt
        // que de retomber silencieusement sur une valeur en dur (« bloquer plutôt qu'envoyer faux »).
        var act = () => new ServiceCollection().AddFacturXModule(ConfigWith(licenseType: null));

        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*QuestPdf:LicenseType*");
    }

    [Fact]
    public void AddFacturXModule_IsIdempotent()
    {
        var services = new ServiceCollection();
        var configuration = ConfigWith("Community");

        var act = () =>
        {
            services.AddFacturXModule(configuration);
            services.AddFacturXModule(configuration);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void AddFacturXModule_ResolvesFacturXBuilder_WithItsSerializerDependency()
    {
        // Garde le câblage DI complet du builder (FX04) : si l'enregistrement du sérialiseur CII était
        // retiré/réordonné, la résolution de IFacturXBuilder casserait au runtime — ici elle est exercée.
        using var provider = new ServiceCollection().AddFacturXModule(ConfigWith("Community")).BuildServiceProvider();

        provider.GetRequiredService<IFacturXBuilder>().Should().BeOfType<FacturXBuilder>();
        provider.GetRequiredService<ICrossIndustryInvoiceSerializer>().Should().BeOfType<CrossIndustryInvoiceSerializer>();
    }
}

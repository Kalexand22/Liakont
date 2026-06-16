namespace Liakont.Modules.FacturX.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.FacturX.Application;
using Liakont.Modules.FacturX.Application.Cii;
using Liakont.Modules.FacturX.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using Xunit;

/// <summary>
/// Comportement de l'enregistrement DI du module FacturX (FX02) : <c>AddFacturXModule</c> est l'API de
/// composition publique du module, à effet de bord (activation de la licence QuestPDF Community). Elle
/// chaîne et reste idempotente (le socle peut déjà avoir posé la licence — FacturX ne suppose pas
/// l'ordre).
/// </summary>
public sealed class FacturXModuleRegistrationTests
{
    [Fact]
    public void AddFacturXModule_ReturnsSameCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddFacturXModule();

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void AddFacturXModule_ActivatesQuestPdfCommunityLicense()
    {
        new ServiceCollection().AddFacturXModule();

        QuestPDF.Settings.License.Should().Be(LicenseType.Community);
    }

    [Fact]
    public void AddFacturXModule_IsIdempotent()
    {
        var services = new ServiceCollection();

        var act = () =>
        {
            services.AddFacturXModule();
            services.AddFacturXModule();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void AddFacturXModule_ResolvesFacturXBuilder_WithItsSerializerDependency()
    {
        // Garde le câblage DI complet du builder (FX04) : si l'enregistrement du sérialiseur CII était
        // retiré/réordonné, la résolution de IFacturXBuilder casserait au runtime — ici elle est exercée.
        using var provider = new ServiceCollection().AddFacturXModule().BuildServiceProvider();

        provider.GetRequiredService<IFacturXBuilder>().Should().BeOfType<FacturXBuilder>();
        provider.GetRequiredService<ICrossIndustryInvoiceSerializer>().Should().BeOfType<CrossIndustryInvoiceSerializer>();
    }
}

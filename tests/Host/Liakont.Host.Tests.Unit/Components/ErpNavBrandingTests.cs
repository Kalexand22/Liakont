namespace Liakont.Host.Tests.Unit.Components;

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Liakont.Host;
using Liakont.Host.Components.Layout;
using Liakont.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit de la marque d'instance dans la barre latérale (BRD01, marque grise) : le nom commercial
/// remplace la marque socle « Stratum ERP », et un logo optionnel s'affiche quand l'instance le configure.
/// </summary>
public sealed class ErpNavBrandingTests : BunitContext
{
    public ErpNavBrandingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddCommonUI(); // ICommandRegistry / IShortcutService / StratumIcon requis par ErpNav.
        Services.AddSingleton<IStringLocalizer<HostResources>>(new StubHostLocalizer());
    }

    private void WithBranding(BrandingOptions branding) =>
        Services.AddSingleton<IOptions<BrandingOptions>>(Options.Create(branding));

    [Fact]
    public void Brand_Text_Is_The_Commercial_Name()
    {
        WithBranding(new BrandingOptions { CommercialName = "Acme Facture" });

        var cut = Render<ErpNav>();

        cut.Find(".erp-nav-brand").TextContent.Should().Be("Acme Facture");
    }

    [Fact]
    public void Empty_Commercial_Name_Falls_Back_To_Default_Brand()
    {
        // Mauvaise config (CommercialName vidé) : la marque de la barre latérale replie sur « Liakont ».
        WithBranding(new BrandingOptions { CommercialName = string.Empty });

        var cut = Render<ErpNav>();

        cut.Find(".erp-nav-brand").TextContent.Should().Be(BrandingOptions.DefaultCommercialName);
    }

    [Fact]
    public void Logo_Is_Rendered_When_Configured()
    {
        WithBranding(new BrandingOptions { CommercialName = "Acme", LogoUrl = "/branding/acme.svg" });

        var cut = Render<ErpNav>();

        var logo = cut.Find("img.erp-nav-logo");
        logo.GetAttribute("src").Should().Be("/branding/acme.svg");
        logo.GetAttribute("alt").Should().Be("Acme");
    }

    [Fact]
    public void No_Logo_When_Not_Configured()
    {
        WithBranding(new BrandingOptions { CommercialName = "Acme" });

        var cut = Render<ErpNav>();

        cut.FindAll("img.erp-nav-logo").Should().BeEmpty();
    }

    private sealed class StubHostLocalizer : IStringLocalizer<HostResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, name, resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}

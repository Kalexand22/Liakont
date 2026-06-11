namespace Liakont.Host.Tests.Unit.Components;

using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Layout;
using Liakont.Host.Configuration;
using Liakont.Modules.Archive.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Tests bUnit du branding d'instance dans le &lt;head&gt; (BRD01, marque grise) : titre d'onglet = nom
/// commercial, favicon optionnel, surcharge des jetons de thème à partir de couleurs hex VALIDÉES. Anti-faux-
/// vert : on prouve qu'une couleur malformée est IGNORÉE (aucun bloc &lt;style&gt;), pas injectée telle quelle.
/// </summary>
public sealed class BrandingHeadTests : BunitContext
{
    private void WithBranding(BrandingOptions branding) =>
        Services.AddSingleton<IOptions<BrandingOptions>>(Options.Create(branding));

    [Fact]
    public void Title_Is_The_Commercial_Name()
    {
        WithBranding(new BrandingOptions { CommercialName = "Acme Facture" });

        var cut = Render<BrandingHead>();

        cut.Find("title").TextContent.Should().Be("Acme Facture");
    }

    [Fact]
    public void Defaults_Render_No_Favicon_And_No_Color_Override()
    {
        // Marque par défaut « Liakont » : titre rendu, mais aucun favicon ni surcharge de thème (champs vides).
        WithBranding(new BrandingOptions());

        var cut = Render<BrandingHead>();

        cut.Find("title").TextContent.Should().Be(BrandingOptions.DefaultCommercialName);
        cut.FindAll("link[rel=icon]").Should().BeEmpty();
        cut.FindAll("style").Should().BeEmpty();
    }

    [Fact]
    public void Empty_Commercial_Name_Falls_Back_To_Default_In_Title()
    {
        // Mauvaise config : un opérateur vide Branding:CommercialName (chaîne vide liée par-dessus le
        // défaut C#). Le titre d'onglet doit replier sur « Liakont », jamais rester blanc.
        WithBranding(new BrandingOptions { CommercialName = "   " });

        var cut = Render<BrandingHead>();

        cut.Find("title").TextContent.Should().Be(BrandingOptions.DefaultCommercialName);
    }

    [Fact]
    public void Favicon_Link_Is_Emitted_When_Configured()
    {
        WithBranding(new BrandingOptions { FaviconUrl = "/branding/acme.ico" });

        var cut = Render<BrandingHead>();

        var icon = cut.Find("link[rel=icon]");
        icon.GetAttribute("href").Should().Be("/branding/acme.ico");
    }

    [Fact]
    public void Valid_Colors_Override_Theme_Tokens()
    {
        WithBranding(new BrandingOptions { PrimaryColor = "#123456", AccentColor = "#abcdef" });

        var cut = Render<BrandingHead>();

        string style = cut.Find("style").TextContent;
        style.Should().Contain(":root{");
        style.Should().Contain("--color-primary:#123456;");
        style.Should().Contain("--color-primary-600:#123456;", "la barre latérale dérive de --color-primary-600");
        style.Should().Contain("--color-primary-container:#abcdef;", "l'accent surcharge le conteneur primaire");
    }

    [Fact]
    public void Default_Commercial_Name_Is_Consistent_Across_Branding_Readers()
    {
        // Garde anti-divergence (BRD01) : la marque produit par défaut doit rester identique des deux
        // côtés — coquille/emails via BrandingOptions, notice d'export via ReversibilityBranding — bien
        // que la section "Branding" soit lue par deux chemins (isolation de module : Archive ne dépend
        // pas du Host). Si l'un des défauts change sans l'autre, ce test échoue.
        ReversibilityBranding.DefaultCommercialName.Should().Be(BrandingOptions.DefaultCommercialName);
    }

    [Fact]
    public void Malformed_Color_Is_Ignored_No_Style_Block()
    {
        // Valeur de config malformée (pas un hex valide) : ignorée, aucun bloc <style> émis (pas d'injection).
        WithBranding(new BrandingOptions { PrimaryColor = "</style><script>alert(1)</script>" });

        var cut = Render<BrandingHead>();

        cut.FindAll("style").Should().BeEmpty();
        cut.Markup.Should().NotContain("<script>");
    }
}

namespace Liakont.Host.Tests.Unit.Pages;

using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Test bUnit du contenu « page introuvable » rendu par le &lt;NotFound&gt; du routeur (FIX07c) : une route
/// inconnue doit afficher un message opérateur français explicite avec action corrective — jamais une page
/// entièrement vide (bug recette GATE_CONSOLE_WEB). Anti-faux-vert : le message et le lien de retour sont
/// réellement rendus.
/// </summary>
public sealed class PageNotFoundTests : BunitContext
{
    public PageNotFoundTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Renders_An_Explicit_French_Not_Found_Message()
    {
        var cut = Render<PageNotFound>();

        var content = cut.Find("[data-testid='page-not-found']");
        content.TextContent.Should().Contain("introuvable");
    }

    [Fact]
    public void Offers_A_Corrective_Action_Back_To_The_Dashboard()
    {
        var cut = Render<PageNotFound>();

        var home = cut.Find("[data-testid='page-not-found-home']");
        home.GetAttribute("href").Should().Be("/");
    }
}

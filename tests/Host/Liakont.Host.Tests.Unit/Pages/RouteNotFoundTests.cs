namespace Liakont.Host.Tests.Unit.Pages;

using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit du câblage de route inconnue (<see cref="RouteNotFound"/>) rendu par le &lt;NotFound&gt; du
/// routeur (FIX07c). Branche sécurité : un visiteur NON authentifié est redirigé vers /login (aucun contenu
/// applicatif rendu à un anonyme) ; un utilisateur authentifié voit le message « page introuvable » explicite
/// — jamais une page vide. Anti-faux-vert : les deux branches sont réellement exercées (redirection effective
/// vs message effectivement rendu).
/// </summary>
public sealed class RouteNotFoundTests : BunitContext
{
    public RouteNotFoundTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Authenticated_User_Sees_The_Not_Found_Message()
    {
        AddAuthorization().SetAuthorized("operateur");

        var cut = Render<RouteNotFound>();

        cut.FindAll("[data-testid='page-not-found']").Should().ContainSingle();
        cut.Services.GetRequiredService<NavigationManager>().Uri.Should().NotEndWith("/login");
    }

    [Fact]
    public void Anonymous_Visitor_Is_Redirected_To_Login()
    {
        AddAuthorization().SetNotAuthorized();

        var cut = Render<RouteNotFound>();

        // Aucun contenu applicatif rendu à un anonyme ; redirection effective vers la connexion.
        cut.FindAll("[data-testid='page-not-found']").Should().BeEmpty();
        cut.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/login");
    }
}

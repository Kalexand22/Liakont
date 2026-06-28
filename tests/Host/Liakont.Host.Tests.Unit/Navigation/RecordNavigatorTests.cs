namespace Liakont.Host.Tests.Unit.Navigation;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Navigation;
using Xunit;

// BUG-19 — barre de navigation préc/suiv transverse en vue détail. Affichée seulement quand la fiche courante
// appartient à la dernière liste parcourue ; bornes (1er/dernier) désactivées ; « Suivant » navigue.
public sealed class RecordNavigatorTests : BunitContext
{
    public RecordNavigatorTests()
    {
        // StratumButton (RadzenButton) peut appeler du JS : mode permissif.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Renders_nothing_without_a_captured_list_context()
    {
        Services.AddScoped<IListNavigationContext, ListNavigationContext>();

        var cut = Render<RecordNavigator>();

        cut.FindAll("[data-testid='record-navigator']").Should().BeEmpty("hors d'une liste parcourue, aucune navigation n'est affichée");
    }

    [Fact]
    public void Renders_prev_next_and_position_in_the_middle_of_the_list()
    {
        UseContext(["/documents/a", "/documents/b", "/documents/c"]);
        NavigateTo("/documents/b");

        var cut = Render<RecordNavigator>();

        cut.FindAll("[data-testid='record-navigator']").Should().ContainSingle();
        cut.Find("[data-testid='record-nav-position']").TextContent.Should().Contain("2 / 3");
        cut.Find("[data-testid='record-nav-prev']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='record-nav-next']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Disables_previous_at_the_first_record()
    {
        UseContext(["/documents/a", "/documents/b"]);
        NavigateTo("/documents/a");

        var cut = Render<RecordNavigator>();

        cut.Find("[data-testid='record-nav-prev']").HasAttribute("disabled").Should().BeTrue("le premier enregistrement n'a pas de précédent");
        cut.Find("[data-testid='record-nav-next']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Disables_next_at_the_last_record()
    {
        UseContext(["/documents/a", "/documents/b"]);
        NavigateTo("/documents/b");

        var cut = Render<RecordNavigator>();

        cut.Find("[data-testid='record-nav-next']").HasAttribute("disabled").Should().BeTrue("le dernier enregistrement n'a pas de suivant");
        cut.Find("[data-testid='record-nav-prev']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Next_button_navigates_to_the_following_record_without_returning_to_the_grid()
    {
        UseContext(["/documents/a", "/documents/b", "/documents/c"]);
        var nav = NavigateTo("/documents/b");

        var cut = Render<RecordNavigator>();
        cut.Find("[data-testid='record-nav-next']").Click();

        nav.Uri.Should().EndWith("/documents/c", "« Suivant » navigue directement vers l'enregistrement suivant de la liste");
    }

    private void UseContext(string[] orderedDetailUrls)
    {
        var ctx = new ListNavigationContext();
        ctx.Capture(orderedDetailUrls);
        Services.AddScoped<IListNavigationContext>(_ => ctx);
    }

    private NavigationManager NavigateTo(string relativeUrl)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(relativeUrl);
        return nav;
    }
}

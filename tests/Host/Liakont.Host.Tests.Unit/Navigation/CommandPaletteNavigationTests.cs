namespace Liakont.Host.Tests.Unit.Navigation;

using System.Collections.Generic;
using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Xunit;

/// <summary>
/// Épingle la correction socle consignée en provenance §4.23 : la palette de recherche (Ctrl+K)
/// doit voir les arbres déclarés en <see cref="INavNodeProvider"/> (sous-menu Paramétrage du lot
/// polish UX/UI), pas seulement les sections plates — et collecter leurs feuilles récursivement.
/// Sans ces tests, un refactor de <c>BuildNavTree</c>/<c>CollectSearchableItems</c> pourrait faire
/// disparaître silencieusement les entrées de sous-menu de la recherche globale.
/// </summary>
public sealed class CommandPaletteNavigationTests : BunitContext
{
    public CommandPaletteNavigationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    private static NavNode LiakontLikeTree() => new()
    {
        Label = "Liakont",
        Order = 5,
        Children =
        [
            new NavNode { Label = "Documents", Href = "/documents" },
            new NavNode
            {
                Label = "Paramétrage",
                Children =
                [
                    new NavNode { Label = "Vue d'ensemble", Href = "/parametrage", ExactMatch = true },
                    new NavNode { Label = "Paramètres fiscaux", Href = "/parametrage/fiscal" },
                ],
            },
        ],
    };

    [Fact]
    public void GlobalShortcutHandler_Should_Feed_The_Palette_With_Node_Provider_Trees()
    {
        Services.AddScoped<INavSectionProvider, FlatSectionProvider>();
        Services.AddScoped<INavNodeProvider, TreeNodeProvider>();

        var cut = Render<GlobalShortcutHandler>();

        // La palette reçoit les DEUX sources : sections plates ET arbres INavNodeProvider —
        // y compris la feuille du sous-menu (c'est la ligne corrigée du socle).
        var navNodes = cut.FindComponent<CommandPalette>().Instance.NavNodes;
        navNodes.Should().Contain(n => n.Label == "Accueil");
        var liakont = navNodes.Single(n => n.Label == "Liakont");
        liakont.Children.Single(c => c.Label == "Paramétrage")
            .Children.Should().Contain(c => c.Href == "/parametrage/fiscal");
    }

    [Fact]
    public void CommandPalette_Should_List_Sub_Menu_Leaves_Recursively()
    {
        // Palette OUVERTE sans saisie : tous les items de navigation sont listés — la feuille du
        // sous-menu doit y figurer (collecte récursive de CollectSearchableItems).
        var cut = Render<CommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.NavNodes, (IReadOnlyList<NavNode>)[LiakontLikeTree()]));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='command-palette-results']").TextContent
                .Should().Contain("Paramètres fiscaux", "la feuille du sous-menu est recherchable"));
    }

    private sealed class FlatSectionProvider : INavSectionProvider
    {
        public NavSection GetSection() =>
            new("Accueil", "bi-grid-1x2", 0, [new NavItem("Tableau de bord", "/")]);
    }

    private sealed class TreeNodeProvider : INavNodeProvider
    {
        public NavNode GetNavNode() => LiakontLikeTree();
    }
}

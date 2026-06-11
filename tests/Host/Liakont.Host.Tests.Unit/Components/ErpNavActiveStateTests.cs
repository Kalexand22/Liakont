namespace Liakont.Host.Tests.Unit.Components;

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Liakont.Host;
using Liakont.Host.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Stratum.Common.UI;
using Stratum.Common.UI.Models;
using Xunit;

/// <summary>
/// Test bUnit de l'état actif EXCLUSIF de la navigation latérale (FIX209, décision E5). Le bug socle de
/// recette : sur une route enfant, une feuille parente dont le href est PRÉFIXE de la route (ex.
/// /admin/audit) restait surlignée EN MÊME TEMPS que la feuille enfant (/admin/audit/policies). On prouve
/// qu'une SEULE entrée est surlignée — la plus spécifique. Anti-faux-vert : la bonne feuille est réellement
/// active, et il n'y en a qu'une (pas deux <c>.active</c>).
/// </summary>
public sealed class ErpNavActiveStateTests : BunitContext
{
    public ErpNavActiveStateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddCommonUI(); // ICommandRegistry / IShortcutService / StratumIcon requis par ErpNav.
        Services.AddSingleton<IStringLocalizer<HostResources>>(new StubHostLocalizer());

        // Une section avec deux feuilles dont l'une (/admin/audit) est PRÉFIXE de l'autre
        // (/admin/audit/policies) : c'est exactement le scénario du bug d'état actif non exclusif.
        Services.AddSingleton<INavSectionProvider>(new TwoLeafNavSectionProvider());
    }

    [Fact]
    public void Only_The_Most_Specific_Leaf_Is_Active_On_Child_Route()
    {
        Navigate("/admin/audit/policies");

        var cut = Render<ErpNav>();

        var active = cut.FindAll("a.erp-nav-leaf.active");
        active.Should().ContainSingle("une seule entrée doit être surlignée (état actif exclusif)");
        active[0].GetAttribute("data-testid").Should().Be("nav-link-admin-audit-policies");
    }

    [Fact]
    public void Parent_Leaf_Is_Active_On_Its_Own_Route()
    {
        Navigate("/admin/audit");

        var cut = Render<ErpNav>();

        var active = cut.FindAll("a.erp-nav-leaf.active");
        active.Should().ContainSingle("la feuille parente seule est active sur sa propre route");
        active[0].GetAttribute("data-testid").Should().Be("nav-link-admin-audit");
    }

    private void Navigate(string relativeUrl)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUrl);
    }

    private sealed class TwoLeafNavSectionProvider : INavSectionProvider
    {
        public NavSection GetSection() => new(
            Title: "Audit",
            Icon: "bi-journal-text",
            Order: 70,
            Items:
            [
                new NavItem("Journal d'audit", "/admin/audit"),
                new NavItem("Politiques", "/admin/audit/policies"),
            ]);
    }

    private sealed class StubHostLocalizer : IStringLocalizer<HostResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, name, resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}

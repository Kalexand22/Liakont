namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Ged;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Rendu PUR de la recherche documentaire GED (GED09a, F19 §6.7) : la vue reçoit ses résultats + facettes en
/// paramètre et remonte les intentions (recherche, facette, retrait de filtre, page suivante) par EventCallback —
/// aucune logique métier, aucun accès index/base. Le masquage de confidentialité est SERVER-SIDE (la vue n'a jamais
/// à décider quoi masquer). 100 % français.
/// </summary>
public sealed class GedSearchViewTests : BunitContext
{
    public GedSearchViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    private static GedSearchResults Results(
        IReadOnlyList<GedSearchHit>? hits = null,
        IReadOnlyList<GedSearchFacet>? facets = null,
        Guid? nextCursor = null) => new()
        {
            Hits = hits ?? [],
            Facets = facets ?? [],
            NextCursor = nextCursor,
        };

    [Fact]
    public void Before_Any_Search_Shows_A_Hint_And_No_Results()
    {
        var cut = Render<GedSearchView>(p => p
            .Add(v => v.Results, GedSearchResults.Empty)
            .Add(v => v.HasSearched, false));

        cut.Find("[data-testid='ged-search-hint']").TextContent.Should().Contain("Saisissez");
        cut.FindAll("[data-testid='ged-search-empty']").Should().BeEmpty();
        cut.FindAll(".ged-search__hit").Should().BeEmpty();
    }

    [Fact]
    public void Submitting_The_Query_Raises_OnSearch_With_The_Typed_Text()
    {
        string? searched = null;
        var cut = Render<GedSearchView>(p => p
            .Add(v => v.Results, GedSearchResults.Empty)
            .Add(v => v.OnSearch, EventCallback.Factory.Create<string>(this, q => searched = q)));

        cut.Find("[data-testid='ged-search-input']").Input("facture 2026");
        cut.Find("[data-testid='ged-search-submit']").Click();

        searched.Should().Be("facture 2026");
    }

    [Fact]
    public void Renders_Hits_With_Title_A_Document_Link_And_A_French_Status_Badge()
    {
        var id = Guid.NewGuid();
        var cut = Render<GedSearchView>(p => p
            .Add(v => v.Results, Results(hits: [new GedSearchHit(id, "Bordereau 42", "bordereau", "indexed")]))
            .Add(v => v.HasSearched, true));

        var hit = cut.Find($"[data-testid='ged-hit-{id}']");
        hit.TextContent.Should().Contain("Bordereau 42");
        hit.TextContent.Should().Contain("Indexé");

        var link = cut.Find($"[data-testid='ged-hit-link-{id}']");
        link.GetAttribute("href").Should().Be($"/ged/document/{id}");
    }

    [Fact]
    public void Maps_Deferred_Status_To_Its_French_Label()
    {
        var id = Guid.NewGuid();
        var cut = Render<GedSearchView>(p => p
            .Add(v => v.Results, Results(hits: [new GedSearchHit(id, "Sans profil", null, "deferred")]))
            .Add(v => v.HasSearched, true));

        cut.Find($"[data-testid='ged-hit-{id}']").TextContent.Should().Contain("En attente de classement");
    }

    [Fact]
    public void After_A_Search_With_No_Hits_Says_No_Document_Matches()
    {
        var cut = Render<GedSearchView>(p => p
            .Add(v => v.Results, GedSearchResults.Empty)
            .Add(v => v.HasSearched, true));

        cut.Find("[data-testid='ged-search-empty']").TextContent.Should().Contain("Aucun document");
        cut.FindAll("[data-testid='ged-search-hint']").Should().BeEmpty();
    }

    [Fact]
    public void Renders_Facets_Grouped_By_Axis_And_Clicking_One_Raises_OnFacetSelected()
    {
        GedAxisFilter? selected = null;
        var cut = Render<GedSearchView>(p => p
            .Add(v => v.Results, Results(
                hits: [new GedSearchHit(Guid.NewGuid(), "Doc", null, "indexed")],
                facets:
                [
                    new GedSearchFacet("annee", "2026", 12),
                    new GedSearchFacet("annee", "2025", 3),
                ]))
            .Add(v => v.HasSearched, true)
            .Add(v => v.OnFacetSelected, EventCallback.Factory.Create<GedAxisFilter>(this, f => selected = f)));

        cut.Find("[data-testid='ged-facet-group-annee']").Should().NotBeNull();
        var facet = cut.Find("[data-testid='ged-facet-annee-2026']");
        facet.TextContent.Should().Contain("2026").And.Contain("12");

        facet.Click();

        selected.Should().Be(new GedAxisFilter("annee", "2026"));
    }

    [Fact]
    public void Active_Filters_Render_As_Chips_And_Removing_One_Raises_OnFilterRemoved()
    {
        GedAxisFilter? removed = null;
        var cut = Render<GedSearchView>(p => p
            .Add(v => v.Results, GedSearchResults.Empty)
            .Add(v => v.HasSearched, true)
            .Add(v => v.ActiveFilters, new[] { new GedAxisFilter("acheteur", "Dupont") })
            .Add(v => v.OnFilterRemoved, EventCallback.Factory.Create<GedAxisFilter>(this, f => removed = f)));

        var chip = cut.Find("[data-testid='ged-filter-acheteur-Dupont']");
        chip.TextContent.Should().Contain("acheteur").And.Contain("Dupont");

        chip.Click();

        removed.Should().Be(new GedAxisFilter("acheteur", "Dupont"));
    }

    [Fact]
    public void Load_More_Button_Appears_Only_With_A_Next_Cursor_And_Raises_OnLoadMore()
    {
        var withoutCursor = Render<GedSearchView>(p => p
            .Add(v => v.Results, Results(hits: [new GedSearchHit(Guid.NewGuid(), "Doc", null, "indexed")]))
            .Add(v => v.HasSearched, true));
        withoutCursor.FindAll("[data-testid='ged-search-more']").Should().BeEmpty();

        var loadedMore = false;
        var withCursor = Render<GedSearchView>(p => p
            .Add(v => v.Results, Results(
                hits: [new GedSearchHit(Guid.NewGuid(), "Doc", null, "indexed")],
                nextCursor: Guid.NewGuid()))
            .Add(v => v.HasSearched, true)
            .Add(v => v.OnLoadMore, EventCallback.Factory.Create(this, () => loadedMore = true)));

        withCursor.Find("[data-testid='ged-search-more']").Click();

        loadedMore.Should().BeTrue();
    }

    [Fact]
    public void A_Failed_Search_Shows_A_French_Error_Banner_And_No_Results()
    {
        var cut = Render<GedSearchView>(p => p
            .Add(v => v.Results, GedSearchResults.Empty)
            .Add(v => v.HasSearched, true)
            .Add(v => v.LoadFailed, true));

        cut.Find("[data-testid='ged-search-error']").TextContent.Should().Contain("indisponible");
        cut.FindAll("[data-testid='ged-search-empty']").Should().BeEmpty();
    }
}

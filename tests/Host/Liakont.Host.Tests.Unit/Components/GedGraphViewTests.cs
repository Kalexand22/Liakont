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
/// Rendu PUR de l'exploration de graphe GED (GED09c, F19 §6.7) : la vue reçoit ses documents atteignables en
/// paramètre et remonte l'intention « page suivante » par EventCallback — aucune logique métier, aucun accès
/// index/base. Le masquage de confidentialité (racine + extrémités) est SERVER-SIDE (la vue n'a jamais à décider
/// quoi masquer/traverser). 100 % français.
/// </summary>
public sealed class GedGraphViewTests : BunitContext
{
    public GedGraphViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    private static GedGraphResults Results(
        IReadOnlyList<GedGraphHit>? hits = null,
        GedGraphCursor? nextCursor = null) => new()
        {
            Hits = hits ?? [],
            NextCursor = nextCursor,
        };

    [Fact]
    public void Always_Renders_The_Heading_And_The_Explored_Root()
    {
        var id = Guid.NewGuid();
        var cut = Render<GedGraphView>(p => p
            .Add(v => v.Results, GedGraphResults.Empty)
            .Add(v => v.EntityType, "entreprise")
            .Add(v => v.RootId, id));

        cut.Find("[data-testid='ged-graph-heading']").TextContent.Should().Contain("Exploration");
        var root = cut.Find("[data-testid='ged-graph-root']").TextContent;
        root.Should().Contain("entreprise").And.Contain(id.ToString());
    }

    [Fact]
    public void While_Busy_And_Empty_Shows_A_Loading_Indicator()
    {
        var cut = Render<GedGraphView>(p => p
            .Add(v => v.Results, GedGraphResults.Empty)
            .Add(v => v.Busy, true));

        cut.Find("[data-testid='ged-graph-loading']").TextContent.Should().Contain("en cours");
        cut.FindAll("[data-testid='ged-graph-empty']").Should().BeEmpty();
    }

    [Fact]
    public void After_An_Exploration_With_No_Documents_Says_Nothing_Is_Attached()
    {
        var cut = Render<GedGraphView>(p => p
            .Add(v => v.Results, GedGraphResults.Empty)
            .Add(v => v.Busy, false));

        cut.Find("[data-testid='ged-graph-empty']").TextContent.Should().Contain("Aucun document");
        cut.FindAll("[data-testid='ged-graph-loading']").Should().BeEmpty();
    }

    [Fact]
    public void Renders_Reachable_Documents_With_A_Document_Link_Role_And_Depth()
    {
        var docId = Guid.NewGuid();
        var cut = Render<GedGraphView>(p => p
            .Add(v => v.Results, Results(hits: [new GedGraphHit(docId, Guid.NewGuid(), "emitter", 2)])));

        var hit = cut.Find($"[data-testid='ged-graph-hit-{docId}']");
        hit.TextContent.Should().Contain("emitter").And.Contain("2 lien");

        var link = cut.Find($"[data-testid='ged-graph-hit-link-{docId}']");
        link.GetAttribute("href").Should().Be($"/ged/document/{docId}");
    }

    [Fact]
    public void Depth_Zero_Is_Labelled_As_The_Object_Itself()
    {
        var docId = Guid.NewGuid();
        var cut = Render<GedGraphView>(p => p
            .Add(v => v.Results, Results(hits: [new GedGraphHit(docId, Guid.NewGuid(), "subject", 0)])));

        cut.Find($"[data-testid='ged-graph-hit-{docId}']").TextContent.Should().Contain("objet lui-même");
    }

    [Fact]
    public void Load_More_Button_Appears_Only_With_A_Next_Cursor_And_Raises_OnLoadMore()
    {
        var withoutCursor = Render<GedGraphView>(p => p
            .Add(v => v.Results, Results(hits: [new GedGraphHit(Guid.NewGuid(), Guid.NewGuid(), "emitter", 1)])));
        withoutCursor.FindAll("[data-testid='ged-graph-more']").Should().BeEmpty();

        var loadedMore = false;
        var withCursor = Render<GedGraphView>(p => p
            .Add(v => v.Results, Results(
                hits: [new GedGraphHit(Guid.NewGuid(), Guid.NewGuid(), "emitter", 1)],
                nextCursor: new GedGraphCursor(Guid.NewGuid(), Guid.NewGuid(), "emitter")))
            .Add(v => v.OnLoadMore, EventCallback.Factory.Create(this, () => loadedMore = true)));

        withCursor.Find("[data-testid='ged-graph-more']").Click();

        loadedMore.Should().BeTrue();
    }

    [Fact]
    public void While_Busy_The_Load_More_Button_Is_Disabled()
    {
        // Garde de ré-entrance (P2 review) : pendant qu'une requête est en vol, « Charger plus » est désactivé —
        // pas de page keyset redemandée avec un curseur non encore avancé.
        var cut = Render<GedGraphView>(p => p
            .Add(v => v.Results, Results(
                hits: [new GedGraphHit(Guid.NewGuid(), Guid.NewGuid(), "emitter", 1)],
                nextCursor: new GedGraphCursor(Guid.NewGuid(), Guid.NewGuid(), "emitter")))
            .Add(v => v.Busy, true));

        cut.Find("[data-testid='ged-graph-more']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void A_Failed_Exploration_Shows_A_French_Error_Banner_And_No_Results()
    {
        var cut = Render<GedGraphView>(p => p
            .Add(v => v.Results, GedGraphResults.Empty)
            .Add(v => v.LoadFailed, true));

        cut.Find("[data-testid='ged-graph-error']").TextContent.Should().Contain("indisponible");
        cut.FindAll("[data-testid='ged-graph-empty']").Should().BeEmpty();
        cut.FindAll("[data-testid='ged-graph-loading']").Should().BeEmpty();
    }
}

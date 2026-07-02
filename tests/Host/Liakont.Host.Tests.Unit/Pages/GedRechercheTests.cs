namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Ged;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests bUnit de la page portail « Recherche documentaire » <c>/ged/recherche</c> (GED09a, règle de review
/// n°19 : une page Blazor sans test est un P1). La vue-pure et le seam sont testés séparément
/// (<see cref="Components.GedSearchViewTests"/> / <see cref="Ged.GedSearchQueryServiceTests"/>) ; ICI on exerce
/// la MACHINE À ÉTATS de la page : recherche→affichage, changement de filtre→réinitialisation, pagination
/// keyset→accumulation, échec→bandeau fail-closed (aucun résultat affiché). La garde de ré-entrance est portée
/// par la désactivation des boutons pendant une requête en vol (testée côté vue-pure). Aucune logique métier
/// n'est ici (déléguée au seam <c>IGedQueries</c>).
/// </summary>
public sealed class GedRechercheTests : BunitContext
{
    public GedRechercheTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs();
    }

    [Fact]
    public void Searching_Renders_The_Returned_Hits()
    {
        var id = Guid.NewGuid();
        Services.AddScoped<IGedQueries>(_ => new FakeGedQueries(Page([id])));

        var cut = Render<GedRecherche>();

        // Avant recherche : invite. La liste n'est peuplée qu'après soumission.
        cut.FindAll("[data-testid='ged-search-hint']").Should().ContainSingle();

        cut.Find("[data-testid='ged-search-input']").Input("bordereau");
        cut.Find("[data-testid='ged-search-submit']").Click();

        cut.WaitForAssertion(() => cut.FindAll($"[data-testid='ged-hit-{id}']").Should().ContainSingle());
    }

    [Fact]
    public void Selecting_A_Facet_Resets_The_List_To_The_New_Filtered_Page()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var fake = new FakeGedQueries(
            Page([first], facets: [new GedSearchFacet("annee", "2026", 1)]),
            Page([second]));
        Services.AddScoped<IGedQueries>(_ => fake);

        var cut = Render<GedRecherche>();
        cut.Find("[data-testid='ged-search-input']").Input("x");
        cut.Find("[data-testid='ged-search-submit']").Click();
        cut.WaitForAssertion(() => cut.FindAll($"[data-testid='ged-hit-{first}']").Should().ContainSingle());

        // Cliquer une facette relance une recherche filtrée depuis la 1re page : les hits sont REMPLACÉS.
        cut.Find("[data-testid='ged-facet-annee-2026']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll($"[data-testid='ged-hit-{second}']").Should().ContainSingle();
            cut.FindAll($"[data-testid='ged-hit-{first}']").Should().BeEmpty();
        });
        fake.LastRequest!.AxisFilters.Should().ContainSingle()
            .Which.Should().Be(new GedAxisFilter("annee", "2026"));
    }

    [Fact]
    public void Load_More_Appends_The_Next_Keyset_Page_Without_Losing_The_First()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var fake = new FakeGedQueries(
            Page([firstId], next: Guid.NewGuid()),
            Page([secondId]));
        Services.AddScoped<IGedQueries>(_ => fake);

        var cut = Render<GedRecherche>();
        cut.Find("[data-testid='ged-search-input']").Input("x");
        cut.Find("[data-testid='ged-search-submit']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='ged-search-more']").Should().NotBeNull());

        cut.Find("[data-testid='ged-search-more']").Click();

        cut.WaitForAssertion(() =>
        {
            // Accumulation keyset : la page suivante s'AJOUTE (le curseur exclusif est propagé), la 1re reste.
            cut.FindAll($"[data-testid='ged-hit-{firstId}']").Should().ContainSingle();
            cut.FindAll($"[data-testid='ged-hit-{secondId}']").Should().ContainSingle();
        });
        fake.Requests.Should().HaveCount(2);
        fake.Requests[1].AfterDocumentId.Should().NotBeNull("la 2e requête reprend au curseur keyset de la 1re");
    }

    [Fact]
    public void A_Failed_Search_Shows_The_Error_Banner_And_No_Results()
    {
        Services.AddScoped<IGedQueries>(_ => FakeGedQueries.Throwing());

        var cut = Render<GedRecherche>();
        cut.Find("[data-testid='ged-search-input']").Input("x");
        cut.Find("[data-testid='ged-search-submit']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='ged-search-error']").Should().ContainSingle();
            cut.FindAll(".ged-search__hit").Should().BeEmpty();
        });
    }

    private static GedSearchResults Page(
        IReadOnlyList<Guid> hitIds,
        IReadOnlyList<GedSearchFacet>? facets = null,
        Guid? next = null) => new()
        {
            Hits = hitIds.Select(id => new GedSearchHit(id, "Doc " + id, null, "indexed")).ToList(),
            Facets = facets ?? [],
            NextCursor = next,
        };

    private sealed class FakeGedQueries : IGedQueries
    {
        private readonly Queue<GedSearchResults> _pages;
        private readonly bool _throws;

        public FakeGedQueries(params GedSearchResults[] pages)
        {
            _pages = new Queue<GedSearchResults>(pages);
            _throws = false;
        }

        private FakeGedQueries(bool throws)
        {
            _pages = new Queue<GedSearchResults>();
            _throws = throws;
        }

        public List<GedSearchRequest> Requests { get; } = [];

        public GedSearchRequest? LastRequest => Requests.Count > 0 ? Requests[^1] : null;

        public static FakeGedQueries Throwing() => new(throws: true);

        public Task<GedSearchResults> SearchAsync(GedSearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (_throws)
            {
                // Levée synchrone : le call est dans le try de la page (comme le fake B2C) → catch → bandeau.
                throw new InvalidOperationException("Échec simulé de la recherche GED.");
            }

            return Task.FromResult(_pages.Count > 0 ? _pages.Dequeue() : GedSearchResults.Empty);
        }
    }
}

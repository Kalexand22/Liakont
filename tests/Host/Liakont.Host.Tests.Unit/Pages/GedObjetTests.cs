namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Ged;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests bUnit de la page portail « Exploration d'objet » <c>/ged/objet/{entityType}/{id}</c> (GED09c, règle de
/// review n°19 : une page Blazor sans test est un P1). La vue-pure et le seam sont testés séparément
/// (<see cref="Components.GedGraphViewTests"/> / <see cref="Ged.GedGraphQueryServiceTests"/>) ; ICI on exerce la
/// MACHINE À ÉTATS de la page : ouverture→exploration automatique, pagination keyset→accumulation, objet vide→
/// message, échec→bandeau fail-closed (aucun résultat affiché), et la garde de ré-entrance. Aucune logique métier
/// n'est ici (déléguée au seam <c>IGedGraphQueries</c>).
/// </summary>
public sealed class GedObjetTests : BunitContext
{
    public GedObjetTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs();
    }

    [Fact]
    public void Opening_An_Object_Explores_It_And_Renders_The_Reachable_Documents()
    {
        var docId = Guid.NewGuid();
        Services.AddScoped<IGedGraphQueries>(_ => new FakeGedGraphQueries(Page([docId])));

        var cut = Render<GedObjet>(p => p
            .Add(c => c.EntityType, "entreprise")
            .Add(c => c.Id, Guid.NewGuid()));

        // L'exploration démarre à l'ouverture (la racine est connue par la route) : les documents atteignables
        // s'affichent sans geste utilisateur.
        cut.WaitForAssertion(() => cut.FindAll($"[data-testid='ged-graph-hit-{docId}']").Should().ContainSingle());
    }

    [Fact]
    public void An_Object_With_No_Reachable_Documents_Shows_The_Empty_Message()
    {
        Services.AddScoped<IGedGraphQueries>(_ => new FakeGedGraphQueries(GedGraphResults.Empty));

        var cut = Render<GedObjet>(p => p
            .Add(c => c.EntityType, "entreprise")
            .Add(c => c.Id, Guid.NewGuid()));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ged-graph-empty']").Should().ContainSingle());
    }

    [Fact]
    public void Load_More_Appends_The_Next_Keyset_Page_Without_Losing_The_First()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var fake = new FakeGedGraphQueries(
            Page([first], next: new GedGraphCursor(Guid.NewGuid(), Guid.NewGuid(), "emitter")),
            Page([second]));
        Services.AddScoped<IGedGraphQueries>(_ => fake);

        var cut = Render<GedObjet>(p => p
            .Add(c => c.EntityType, "entreprise")
            .Add(c => c.Id, Guid.NewGuid()));
        cut.WaitForAssertion(() => cut.Find("[data-testid='ged-graph-more']").Should().NotBeNull());

        cut.Find("[data-testid='ged-graph-more']").Click();

        cut.WaitForAssertion(() =>
        {
            // Accumulation keyset : la page suivante s'AJOUTE (le curseur exclusif est propagé), la 1re reste.
            cut.FindAll($"[data-testid='ged-graph-hit-{first}']").Should().ContainSingle();
            cut.FindAll($"[data-testid='ged-graph-hit-{second}']").Should().ContainSingle();
        });
        fake.Requests.Should().HaveCount(2);
        fake.Requests[1].After.Should().NotBeNull("la 2e requête reprend au curseur keyset de la 1re");
    }

    [Fact]
    public void A_Failed_Exploration_Shows_The_Error_Banner_And_No_Results()
    {
        Services.AddScoped<IGedGraphQueries>(_ => FakeGedGraphQueries.Throwing());

        var cut = Render<GedObjet>(p => p
            .Add(c => c.EntityType, "entreprise")
            .Add(c => c.Id, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='ged-graph-error']").Should().ContainSingle();
            cut.FindAll(".ged-graph__hit").Should().BeEmpty();
        });
    }

    [Fact]
    public async Task A_Second_Load_More_While_A_Request_Is_In_Flight_Is_Ignored()
    {
        // Garde de ré-entrance (P2 review) exercée jusqu'à la page : après la 1re page, un « Charger plus » est
        // retenu par une porte (requête EN VOL, _isBusy=true), puis une 2e intention est émise. Elle passe par la
        // garde _isBusy de la PAGE (onclick déclenché directement, indépendamment de l'attribut disabled) →
        // no-op : aucune 2e page keyset re-demandée ni trace de consultation dupliquée.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var fake = new FakeGedGraphQueries(
            Page([first], next: new GedGraphCursor(Guid.NewGuid(), Guid.NewGuid(), "emitter")),
            Page([second]));
        Services.AddScoped<IGedGraphQueries>(_ => fake);

        var cut = Render<GedObjet>(p => p
            .Add(c => c.EntityType, "entreprise")
            .Add(c => c.Id, Guid.NewGuid()));
        cut.WaitForAssertion(() => cut.Find("[data-testid='ged-graph-more']").Should().NotBeNull());

        // La page suivante reste EN VOL (porte non relâchée). La 1re exploration (ouverture) est déjà terminée.
        var gate = new TaskCompletionSource();
        fake.Gate = gate;
        var firstMore = cut.Find("[data-testid='ged-graph-more']").TriggerEventAsync("onclick", new MouseEventArgs());
        cut.WaitForState(() => fake.Requests.Count == 2);

        // 2e intention « Charger plus » pendant l'await → garde _isBusy → no-op.
        await cut.Find("[data-testid='ged-graph-more']").TriggerEventAsync("onclick", new MouseEventArgs());

        fake.Requests.Should().HaveCount(2, "une 2e page keyset pendant qu'une requête est en vol est ignorée (garde de ré-entrance)");

        gate.SetResult();
        await firstMore;
        fake.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_Late_Response_From_A_Previous_Object_Does_Not_Contaminate_The_New_Object()
    {
        // Course pilotée par la route (P2 review round 1) : « Charger plus » sur l'objet A reste EN VOL, puis
        // l'utilisateur navigue vers l'objet B (même composant). La réponse tardive de A ne doit PAS s'accumuler
        // sous B (association objet↔document trompeuse évitée) ni corrompre le curseur keyset.
        var rootA = Guid.NewGuid();
        var rootB = Guid.NewGuid();
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        var fake = new RootAwareFakeGraphQueries();
        fake.PagesByRoot[rootA] = new Queue<GedGraphResults>(new[]
        {
            Page([Guid.NewGuid()], next: new GedGraphCursor(Guid.NewGuid(), Guid.NewGuid(), "emitter")),
            Page([docA]),
        });
        fake.PagesByRoot[rootB] = new Queue<GedGraphResults>(new[] { Page([docB]) });
        Services.AddScoped<IGedGraphQueries>(_ => fake);

        var cut = Render<GedObjet>(p => p
            .Add(c => c.EntityType, "entreprise")
            .Add(c => c.Id, rootA));
        cut.WaitForAssertion(() => cut.Find("[data-testid='ged-graph-more']").Should().NotBeNull());

        // Le « Charger plus » de A reste EN VOL (porte posée sur la racine A).
        var gate = new TaskCompletionSource();
        fake.GateForRoot = gate;
        fake.GateRoot = rootA;
        var moreA = cut.Find("[data-testid='ged-graph-more']").TriggerEventAsync("onclick", new MouseEventArgs());
        cut.WaitForState(() => fake.Requests.Count == 2);

        // Navigation vers l'objet B (même composant) : l'exploration de B (non retenue) rend ses documents.
        cut.Render(p => p
            .Add(c => c.EntityType, "entreprise")
            .Add(c => c.Id, rootB));
        cut.WaitForAssertion(() => cut.FindAll($"[data-testid='ged-graph-hit-{docB}']").Should().ContainSingle());

        // Libère la réponse tardive de A : elle doit être IGNORÉE (racine changée).
        gate.SetResult();
        await moreA;

        cut.FindAll($"[data-testid='ged-graph-hit-{docA}']").Should().BeEmpty("la page tardive de l'objet A ne contamine pas l'objet B");
        cut.FindAll($"[data-testid='ged-graph-hit-{docB}']").Should().ContainSingle();
    }

    [Fact]
    public void Opening_An_Object_Writes_Exactly_One_Exploration()
    {
        // « EXACTEMENT une trace explore_entity par consultation » (GDF05) : chaque exploration écrit une trace côté
        // seam (GedGraphQueryService) — l'ouverture ne doit déclencher qu'UN appel. Complète la garde structurelle
        // du prerender (<see cref="The_Object_Page_Disables_Prerender_So_The_Consultation_Trace_Is_Written_Once"/>) :
        // le prerender par défaut instancierait la page deux fois (SSR + circuit) → deux explorations = deux traces.
        var fake = new FakeGedGraphQueries(Page([Guid.NewGuid()]));
        Services.AddScoped<IGedGraphQueries>(_ => fake);

        var cut = Render<GedObjet>(p => p
            .Add(c => c.EntityType, "entreprise")
            .Add(c => c.Id, Guid.NewGuid()));

        cut.WaitForState(() => fake.Requests.Count > 0);
        fake.Requests.Should().ContainSingle("l'ouverture d'un objet ne déclenche qu'une seule exploration (une seule trace)");
    }

    [Fact]
    public void The_Object_Page_Disables_Prerender_So_The_Consultation_Trace_Is_Written_Once()
    {
        // Garde STRUCTURELLE (GDF05) : le double d'audit ('explore_entity', INV-GED-11) provient du prerender qui
        // instancie la page deux fois (passe SSR puis circuit interactif) — un rendu bUnit ne le reproduit pas, donc
        // on vérifie la CAUSE : le rendermode déclaré désactive le prerender (symétrie avec GedDocument, GED09b).
        var mode = typeof(GedObjet).GetCustomAttribute<RenderModeAttribute>()?.Mode;

        mode.Should().BeOfType<InteractiveServerRenderMode>("la page rend en circuit interactif serveur");
        ((InteractiveServerRenderMode)mode!).Prerender.Should().BeFalse(
            "le prerender est désactivé pour n'écrire qu'UNE trace de consultation par ouverture d'objet");
    }

    private static GedGraphResults Page(IReadOnlyList<Guid> docIds, GedGraphCursor? next = null) => new()
    {
        Hits = docIds.Select(d => new GedGraphHit(d, Guid.NewGuid(), "emitter", 1)).ToList(),
        NextCursor = next,
    };

    private sealed class FakeGedGraphQueries : IGedGraphQueries
    {
        private readonly Queue<GedGraphResults> _pages;
        private readonly bool _throws;

        public FakeGedGraphQueries(params GedGraphResults[] pages)
        {
            _pages = new Queue<GedGraphResults>(pages);
            _throws = false;
        }

        private FakeGedGraphQueries(bool throws)
        {
            _pages = new Queue<GedGraphResults>();
            _throws = throws;
        }

        public List<GedGraphRequest> Requests { get; } = [];

        /// <summary>Si posée, l'exploration attend cette porte (simule une requête EN VOL pour la garde de ré-entrance).</summary>
        public TaskCompletionSource? Gate { get; set; }

        public static FakeGedGraphQueries Throwing() => new(throws: true);

        public async Task<GedGraphResults> ExploreAsync(GedGraphRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (Gate is not null)
            {
                await Gate.Task;
            }

            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de l'exploration de graphe GED.");
            }

            return _pages.Count > 0 ? _pages.Dequeue() : GedGraphResults.Empty;
        }
    }

    private sealed class RootAwareFakeGraphQueries : IGedGraphQueries
    {
        public Dictionary<Guid, Queue<GedGraphResults>> PagesByRoot { get; } = new();

        public List<GedGraphRequest> Requests { get; } = [];

        public TaskCompletionSource? GateForRoot { get; set; }

        public Guid GateRoot { get; set; }

        public async Task<GedGraphResults> ExploreAsync(GedGraphRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (GateForRoot is not null && request.RootEntityId == GateRoot)
            {
                await GateForRoot.Task;
            }

            return PagesByRoot.TryGetValue(request.RootEntityId, out var pages) && pages.Count > 0
                ? pages.Dequeue()
                : GedGraphResults.Empty;
        }
    }
}

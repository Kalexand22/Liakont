namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
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
/// Tests bUnit de la page « Fiche document GED » <c>/ged/document/{id}</c> (GED09b, GDF05 ; règle de review n°19 :
/// une page Blazor sans test est un P1). La vue-pure et le seam d'assemblage sont testés séparément
/// (<see cref="Components.GedDocumentViewTests"/> / <see cref="Ged.GedDocumentConsoleQueryServiceTests"/>) ; ICI on
/// exerce la MACHINE À ÉTATS de la page : ouverture→chargement, fiche rendue, introuvable→bandeau, échec→bandeau,
/// et la GARDE ANTI-RÉPONSE-TARDIVE (navigation A→B pendant un chargement en vol — symétrie GDF05 avec la garde
/// forRoot de GedObjet/GED09c). Aucune logique métier n'est ici (déléguée au seam <c>IGedDocumentConsoleQueries</c>).
/// </summary>
public sealed class GedDocumentTests : BunitContext
{
    public GedDocumentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs();
    }

    [Fact]
    public void Opening_A_Document_Renders_Its_Title()
    {
        var id = Guid.NewGuid();
        var fake = new GatedFakeDocumentQueries();
        fake.ModelsById[id] = BuildModel(id, "Bordereau acheteur 42");
        Services.AddScoped<IGedDocumentConsoleQueries>(_ => fake);

        var cut = Render<GedDocument>(p => p.Add(c => c.Id, id));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='ged-document-page-title']").TextContent.Should().Contain("Bordereau acheteur 42"));
    }

    [Fact]
    public void A_Missing_Document_Shows_The_NotFound_Banner()
    {
        // GetAsync renvoie null (document inexistant dans le tenant courant) → bandeau « introuvable », pas de fiche.
        var fake = new GatedFakeDocumentQueries();
        Services.AddScoped<IGedDocumentConsoleQueries>(_ => fake);

        var cut = Render<GedDocument>(p => p.Add(c => c.Id, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='ged-document-notfound']").Should().ContainSingle();
            cut.FindAll("[data-testid='ged-document-page-title']").Should().BeEmpty();
        });
    }

    [Fact]
    public void A_Failed_Load_Shows_The_Error_Banner_And_No_Document()
    {
        // Échec du chargement (index/coffre indisponible OU trace de consultation fail-closed §6.6) : bandeau
        // générique VISIBLE (anti-oracle), aucune fiche affichée.
        var id = Guid.NewGuid();
        var fake = new GatedFakeDocumentQueries();
        fake.Throwing.Add(id);
        Services.AddScoped<IGedDocumentConsoleQueries>(_ => fake);

        var cut = Render<GedDocument>(p => p.Add(c => c.Id, id));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='ged-document-error']").Should().ContainSingle();
            cut.FindAll("[data-testid='ged-document-page-title']").Should().BeEmpty();
        });
    }

    [Fact]
    public async Task A_Late_Response_From_A_Previous_Document_Does_Not_Replace_The_New_Document()
    {
        // Course pilotée par la route (GDF05, symétrie avec la garde forRoot de GedObjet/GED09c) : le chargement du
        // document A reste EN VOL, puis l'utilisateur navigue vers le document B (même composant). La réponse tardive
        // de A ne doit PAS écraser la fiche B affichée — une fiche A sous l'URL B est une association trompeuse
        // (produit de conformité).
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var fake = new GatedFakeDocumentQueries();
        fake.ModelsById[idA] = BuildModel(idA, "Document A");
        fake.ModelsById[idB] = BuildModel(idB, "Document B");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Gate = gate;
        fake.GateId = idA;
        Services.AddScoped<IGedDocumentConsoleQueries>(_ => fake);

        // Ouverture de A : le chargement reste EN VOL (porte posée sur l'id A) → état « chargement ».
        var cut = Render<GedDocument>(p => p.Add(c => c.Id, idA));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ged-document-loading']").Should().ContainSingle());

        // Navigation vers le document B (même composant, non retenu) : sa fiche s'affiche.
        cut.Render(p => p.Add(c => c.Id, idB));
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='ged-document-page-title']").TextContent.Should().Contain("Document B"));

        // Libère la réponse tardive de A, puis draine le dispatcher du renderer : la garde doit l'ÉCARTER (id changé).
        gate.SetResult();
        cut.WaitForState(() => fake.Released.Task.IsCompleted);
        await cut.InvokeAsync(() => Task.CompletedTask);
        await cut.InvokeAsync(() => Task.CompletedTask);

        var title = cut.Find("[data-testid='ged-document-page-title']").TextContent;
        title.Should().Contain("Document B");
        title.Should().NotContain("Document A", "la réponse tardive du document A ne remplace pas la fiche B");
    }

    [Fact]
    public void The_Document_Page_Disables_Prerender_So_The_Consultation_Trace_Is_Written_Once()
    {
        // Garde STRUCTURELLE (GED09b, ancrée par GDF05 pour la symétrie avec GedObjet) : le double de la trace
        // 'view_document' (§6.6) provient du prerender qui instancie la page deux fois (SSR puis circuit interactif) —
        // un rendu bUnit ne le reproduit pas, donc on vérifie la CAUSE : le rendermode déclaré désactive le prerender.
        var mode = typeof(GedDocument).GetCustomAttribute<RenderModeAttribute>()?.Mode;

        mode.Should().BeOfType<InteractiveServerRenderMode>("la page rend en circuit interactif serveur");
        ((InteractiveServerRenderMode)mode!).Prerender.Should().BeFalse(
            "le prerender est désactivé pour n'écrire qu'UNE trace de consultation par ouverture de document");
    }

    private static GedDocumentDetailViewModel BuildModel(Guid id, string title) => new()
    {
        Id = id,
        Title = title,
        DocKind = null,
        Status = "indexed",
        RetentionClass = "tenant_bounded",
        DeferReason = null,
        Integrity = new GedDocumentIntegrityView(GedDocumentIntegrityState.NotArchived, null, null, null),
        PreviewHtml = null,
        IsFiscalLinked = false,
        FiscalDocumentId = null,
        CreatedUtc = new DateTimeOffset(2026, 6, 1, 10, 30, 0, TimeSpan.Zero),
        UpdatedUtc = null,
        Axes = [],
        Entities = [],
    };

    /// <summary>
    /// Double de <see cref="IGedDocumentConsoleQueries"/> : renvoie un modèle par id (ou <see langword="null"/> =
    /// introuvable), peut lever pour un id (échec), et peut RETENIR un chargement sur une porte (<see cref="Gate"/> /
    /// <see cref="GateId"/>) pour simuler une réponse en vol pendant une navigation.
    /// </summary>
    private sealed class GatedFakeDocumentQueries : IGedDocumentConsoleQueries
    {
        public Dictionary<Guid, GedDocumentDetailViewModel?> ModelsById { get; } = new();

        public HashSet<Guid> Throwing { get; } = new();

        public List<Guid> Requests { get; } = [];

        /// <summary>Si posée, le chargement de <see cref="GateId"/> attend cette porte (requête EN VOL).</summary>
        public TaskCompletionSource? Gate { get; set; }

        public Guid GateId { get; set; }

        /// <summary>Signalé quand le chargement RETENU a franchi sa porte (le test sait alors que la réponse tardive arrive).</summary>
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GedDocumentDetailViewModel?> GetAsync(Guid managedDocumentId, CancellationToken cancellationToken = default)
        {
            Requests.Add(managedDocumentId);

            if (Gate is not null && managedDocumentId == GateId)
            {
                await Gate.Task;
                Released.TrySetResult();
            }

            if (Throwing.Contains(managedDocumentId))
            {
                throw new InvalidOperationException("Échec simulé du chargement de la fiche document GED.");
            }

            return ModelsById.TryGetValue(managedDocumentId, out var model) ? model : null;
        }
    }
}

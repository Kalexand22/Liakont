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
    public async Task A_Late_FAILED_Response_From_A_Previous_Document_Does_Not_Replace_The_New_Document()
    {
        // Symétrie du chemin d'ERREUR (garde du catch, GedDocument.LoadAsync) : le chargement de A reste EN VOL puis
        // ÉCHOUE tardivement, APRÈS navigation vers B. La réponse d'échec tardive de A ne doit PAS coller un bandeau
        // d'erreur sous l'URL de B (association trompeuse évitée — produit de conformité).
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var fake = new GatedFakeDocumentQueries();
        fake.Throwing.Add(idA);              // A échoue…
        fake.ModelsById[idB] = BuildModel(idB, "Document B");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Gate = gate;                    // …mais seulement APRÈS la porte (échec en vol)
        fake.GateId = idA;
        Services.AddScoped<IGedDocumentConsoleQueries>(_ => fake);

        var cut = Render<GedDocument>(p => p.Add(c => c.Id, idA));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ged-document-loading']").Should().ContainSingle());

        // Navigation vers le document B (même composant, non retenu) : sa fiche s'affiche.
        cut.Render(p => p.Add(c => c.Id, idB));
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='ged-document-page-title']").TextContent.Should().Contain("Document B"));

        // Libère l'échec tardif de A, puis draine le dispatcher : la garde du catch doit l'ÉCARTER (id changé).
        gate.SetResult();
        cut.WaitForState(() => fake.Released.Task.IsCompleted);
        await cut.InvokeAsync(() => Task.CompletedTask);
        await cut.InvokeAsync(() => Task.CompletedTask);

        cut.FindAll("[data-testid='ged-document-error']").Should().BeEmpty(
            "l'échec tardif du document A ne colle pas de bandeau d'erreur sous l'URL B");
        cut.Find("[data-testid='ged-document-page-title']").TextContent.Should().Contain("Document B");
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

    [Fact]
    public async Task Leaving_The_Page_Cancels_The_Load_Token()
    {
        // P2 GDF12 (2) : la page propage un token lié à SA durée de vie jusqu'au seam (puis Dapper) et l'annule au
        // départ (Dispose) — le chargement de fiche Postgres encore en vol est coupé. CancellationToken.None ne
        // pourrait JAMAIS passer à IsCancellationRequested=true : le voir basculer prouve la propagation d'un VRAI
        // token ET son annulation au départ de la page.
        var id = Guid.NewGuid();
        var fake = new GatedFakeDocumentQueries();
        fake.ModelsById[id] = BuildModel(id, "Bordereau acheteur 42");
        Services.AddScoped<IGedDocumentConsoleQueries>(_ => fake);

        var cut = Render<GedDocument>(p => p.Add(c => c.Id, id));
        cut.WaitForState(() => fake.Requests.Count == 1);
        fake.LastToken.IsCancellationRequested.Should().BeFalse("tant que la page vit, le chargement n'est pas annulé");

        await DisposeComponentsAsync();

        fake.LastToken.IsCancellationRequested.Should().BeTrue("quitter la page annule le chargement (token propagé au seam)");
    }

    [Fact]
    public async Task An_In_Flight_Load_Cancelled_By_Leaving_The_Page_Is_Swallowed_Without_An_Error_Banner()
    {
        // P2 GDF12 (2) — exerce réellement la branche catch (OperationCanceledException) when (...) : une requête
        // EN VOL est coupée par le départ de la page (Dispose annule le token → le seam lève) et la page l'AVALE
        // (retour silencieux, pas de bandeau d'erreur, pas d'état corrompu) — pas seulement la propagation du token.
        var id = Guid.NewGuid();
        var fake = new GatedFakeDocumentQueries { WaitForCancellation = true };
        fake.ModelsById[id] = BuildModel(id, "Doc");
        Services.AddScoped<IGedDocumentConsoleQueries>(_ => fake);

        var cut = Render<GedDocument>(p => p.Add(c => c.Id, id));

        // Requête en vol (le fake n'a pas répondu) : état propre, aucun bandeau d'erreur affiché.
        await fake.InFlight.Task;
        cut.FindAll("[data-testid='ged-document-error']").Should().BeEmpty();

        // Instance et champ privé capturés AVANT le Dispose : après destruction du composant, cut.Instance/FindAll
        // lèvent (ComponentDisposedException) — la réflexion sur la référence déjà capturée reste possible et
        // permet de vérifier l'état interne de la page malgré la destruction du rendu.
        var page = cut.Instance;
        var loadFailedField = page.GetType().GetField("_loadFailed", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Quitter la page annule le token → OperationCanceledException levée en vol → AVALÉE par la page :
        // le Dispose se termine proprement, sans faire remonter d'exception.
        await DisposeComponentsAsync();

        // La branche d'annulation a bien été atteinte (le token en vol a observé l'annulation). On laisse la
        // continuation d'annulation se terminer (elle peut finir juste après le Dispose, sur le pool de threads)
        // puis on vérifie qu'elle n'a PAS été traitée comme un échec : _loadFailed reste false — l'annulation
        // reste bien AVALÉE (retour silencieux), jamais traitée comme un échec.
        fake.LastToken.IsCancellationRequested.Should().BeTrue();
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        loadFailedField.GetValue(page).Should().Be(
            false, "l'annulation par le Dispose est AVALÉE (retour silencieux), jamais traitée comme un échec");
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

        /// <summary>Dernier token passé au seam : sert à vérifier que la page propage son token de durée de vie et l'annule au Dispose (GDF12).</summary>
        public CancellationToken LastToken { get; private set; }

        /// <summary>Si posée, le chargement de <see cref="GateId"/> attend cette porte (requête EN VOL).</summary>
        public TaskCompletionSource? Gate { get; set; }

        public Guid GateId { get; set; }

        /// <summary>Signalé quand le chargement RETENU a franchi sa porte (le test sait alors que la réponse tardive arrive).</summary>
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Si vrai, le chargement reste EN VOL en observant le token (simule une requête coupée par le Dispose, GDF12).</summary>
        public bool WaitForCancellation { get; set; }

        public TaskCompletionSource InFlight { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GedDocumentDetailViewModel?> GetAsync(Guid managedDocumentId, CancellationToken cancellationToken = default)
        {
            Requests.Add(managedDocumentId);
            LastToken = cancellationToken;

            if (WaitForCancellation)
            {
                InFlight.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

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

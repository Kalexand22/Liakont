namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Xunit;

public sealed class DocumentDetailTests : BunitContext
{
    private static readonly Guid DocId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public DocumentDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();

        // La page rend la barre d'actions permanente (FIX04b : verdict garde-fou B2B/B2C + re-vérification via
        // IDocumentControlActions, envoi via IDocumentSendActions) ET le composant de résolution terminale
        // (WEB03c, IDocumentResolutionConsoleService). Par défaut : pas de permission d'action + services no-op
        // (la fiche reste consultable en lecture) ; les tests qui exercent une action remplacent ces fakes.
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: false));
        Services.AddScoped<IDocumentControlActions>(_ => new FakeControlActions());
        Services.AddScoped<IDocumentSendActions>(_ => new FakeSendActions());
        Services.AddScoped<IDocumentResolutionConsoleService>(_ => new NoOpResolutionService());
    }

    [Fact]
    public void Should_Render_Detail_And_Back_Link_When_Document_Found()
    {
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ => FakeDetailQueries.Returning(BuildModel("2026-001")));

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        cut.Find("[data-testid='document-detail-title']").TextContent.Should().Contain("2026-001");
        cut.FindAll("[data-testid='document-detail']").Should().ContainSingle("la vue détail est rendue");
        cut.FindAll("[data-testid='document-detail-back']").Should().ContainSingle("le retour à la liste est proposé");
        cut.FindAll("[data-testid='document-detail-notfound']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-error']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_NotFound_When_Document_Absent()
    {
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ => FakeDetailQueries.Returning(null));

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        cut.FindAll("[data-testid='document-detail-notfound']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail']").Should().BeEmpty("aucune fiche n'est rendue pour un document introuvable");
    }

    [Fact]
    public void Should_Show_Error_Banner_When_Load_Throws()
    {
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ => FakeDetailQueries.Throwing());

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        // L'échec reste VISIBLE (bandeau) et n'expose pas la fiche (anti faux-vert).
        cut.FindAll("[data-testid='document-detail-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Offer_Action_Buttons_In_The_Permanent_Bar_When_Operator_Has_Actions_Permission()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ =>
            FakeDetailQueries.Returning(BuildModel("2026-020", state: "Blocked", companyHint: true)));

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        // Les actions sont dans la barre permanente en tête de fiche — PAS besoin d'ouvrir un onglet (FIX04b).
        cut.FindAll("[data-testid='document-detail-action-bar']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Hide_Action_Buttons_Without_Actions_Permission()
    {
        // Permission par défaut (canAct=false) : la fiche reste consultable, aucun bouton d'action.
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ =>
            FakeDetailQueries.Returning(BuildModel("2026-021", state: "Blocked", companyHint: true)));

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        cut.FindAll("[data-testid='document-detail-action-bar']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-verdict-b2c']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-recheck']").Should().BeEmpty();

        // … mais le blocage reste visible en lecture, dans l'onglet Contrôles (contenu seul).
        SelectControlsTab(cut);
        cut.FindAll("[data-testid='document-detail-controls-blocked']").Should().ContainSingle("le blocage reste visible en lecture");
    }

    [Fact]
    public void Recheck_Click_Calls_The_Action_Service_And_Reloads_The_Detail()
    {
        var actions = new FakeControlActions
        {
            RecheckResult = DocumentControlActionResult.Ok("Re-vérification réussie : le document est maintenant prêt à l'envoi.", "ReadyToSend"),
        };
        var queries = FakeDetailQueries.Returning(BuildModel("2026-022", state: "Blocked", companyHint: true));
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        Services.AddScoped<IDocumentControlActions>(_ => actions);
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ => queries);

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        var loadsBefore = queries.GetDetailCalls;
        cut.Find("[data-testid='document-detail-recheck']").Click();

        cut.WaitForAssertion(() =>
        {
            actions.RecheckCalls.Should().ContainSingle().Which.Should().Be(DocId);

            // La page recharge le détail après l'action (état + historique à jour sans rechargement de page).
            queries.GetDetailCalls.Should().BeGreaterThan(loadsBefore);

            // Le message de retour s'affiche dans le bandeau d'action, en tête de fiche (déplacé depuis l'onglet — FIX04b).
            cut.Find("[data-testid='document-detail-action-feedback']").TextContent.Should().Contain("prêt à l'envoi");
        });
    }

    [Fact]
    public void Verdict_Buttons_Map_To_The_Correct_Console_Verdict()
    {
        // Garde-fou anti-inversion : le bouton B2C doit déclencher ConfirmIndividualB2c, le bouton B2B
        // HandleManuallyB2b (une inversion passerait inaperçue si seul le callback était testé).
        var actions = new FakeControlActions();
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        Services.AddScoped<IDocumentControlActions>(_ => actions);
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ =>
            FakeDetailQueries.Returning(BuildModel("2026-023", state: "Blocked", companyHint: true)));

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        cut.Find("[data-testid='document-detail-verdict-b2c']").Click();
        cut.WaitForAssertion(() => actions.VerdictCalls.Should().ContainSingle());

        cut.Find("[data-testid='document-detail-verdict-b2b']").Click();
        cut.WaitForAssertion(() => actions.VerdictCalls.Should().HaveCount(2));

        actions.VerdictCalls[0].Should().Be((DocId, ConsoleVerdict.ConfirmIndividualB2c));
        actions.VerdictCalls[1].Should().Be((DocId, ConsoleVerdict.HandleManuallyB2b));
    }

    [Fact]
    public void Send_Click_Sends_This_Document_And_Reloads_The_Detail()
    {
        // FIX04b : le bouton « Envoyer » d'un document prêt à l'envoi déclenche l'envoi de CE document
        // (sélection mono-document, chemin d'envoi existant) puis recharge le détail.
        var sender = new FakeSendActions();
        var queries = FakeDetailQueries.Returning(BuildModel("2026-024", state: "ReadyToSend"));
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        Services.AddScoped<IDocumentSendActions>(_ => sender);
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ => queries);

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        var loadsBefore = queries.GetDetailCalls;
        cut.FindAll("[data-testid='document-detail-send']").Should().ContainSingle();
        cut.Find("[data-testid='document-detail-send']").Click();

        cut.WaitForAssertion(() =>
        {
            sender.SendSelectionCalls.Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new[] { DocId });
            queries.GetDetailCalls.Should().BeGreaterThan(loadsBefore);
        });
    }

    private static void SelectControlsTab(IRenderedComponent<DocumentDetail> cut)
    {
        var tab = cut.FindAll("button[role='tab']")
            .Single(b => b.TextContent.Contains("Contrôles", StringComparison.Ordinal));
        tab.Click();
    }

    private static DocumentDetailViewModel BuildModel(string number, string state = "Issued", bool companyHint = false) => new()
    {
        Document = new DocumentDto
        {
            Id = DocId,
            SourceReference = $"src/{number}",
            DocumentNumber = number,
            DocumentType = "invoice",
            IssueDate = new DateOnly(2026, 6, 1),
            CustomerName = "ACME SARL",
            CustomerIsCompanyHint = companyHint,
            TotalNet = 1000m,
            TotalTax = 162.80m,
            TotalGross = 1162.80m,
            State = state,
            PayloadHash = "sha256:payload",
            FirstSeenUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        },
        Events = [],
        BlockingReason = state == "Blocked" ? "L'acheteur semble être un professionnel." : null,
        Archive = null,
        IsArchived = false,
    };

    private sealed class FakeDetailQueries : IDocumentDetailConsoleQueries
    {
        private readonly DocumentDetailViewModel? _model;
        private readonly bool _throws;

        private FakeDetailQueries(DocumentDetailViewModel? model, bool throws)
        {
            _model = model;
            _throws = throws;
        }

        public int GetDetailCalls { get; private set; }

        public static FakeDetailQueries Returning(DocumentDetailViewModel? model) => new(model, throws: false);

        public static FakeDetailQueries Throwing() => new(null, throws: true);

        public Task<DocumentDetailViewModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
        {
            GetDetailCalls++;
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement du détail.");
            }

            return Task.FromResult(_model);
        }
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _canAct;

        public FakePermissionService(bool canAct) => _canAct = canAct;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _canAct && string.Equals(permission, "liakont.actions", StringComparison.Ordinal);
    }

    private sealed class FakeControlActions : IDocumentControlActions
    {
        public List<Guid> RecheckCalls { get; } = [];

        public List<(Guid Id, ConsoleVerdict Verdict)> VerdictCalls { get; } = [];

        public DocumentControlActionResult RecheckResult { get; set; } =
            DocumentControlActionResult.Ok("Re-vérification effectuée.", "Blocked");

        public DocumentControlActionResult VerdictResult { get; set; } =
            DocumentControlActionResult.Ok("Verdict enregistré.", "Blocked");

        public Task<DocumentControlActionResult> SubmitVerdictAsync(Guid documentId, ConsoleVerdict verdict, CancellationToken cancellationToken = default)
        {
            VerdictCalls.Add((documentId, verdict));
            return Task.FromResult(VerdictResult);
        }

        public Task<DocumentControlActionResult> RecheckAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            RecheckCalls.Add(documentId);
            return Task.FromResult(RecheckResult);
        }
    }

    private sealed class FakeSendActions : IDocumentSendActions
    {
        public List<IReadOnlyCollection<Guid>> SendSelectionCalls { get; } = [];

        public DocumentSendActionResult SendResult { get; set; } =
            DocumentSendActionResult.Ok("Envoi déclenché : le traitement d'envoi du tenant émet ce document.");

        public Task<DocumentSendActionResult> SendSelectionAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken = default)
        {
            SendSelectionCalls.Add(documentIds);
            return Task.FromResult(SendResult);
        }

        public Task<DocumentSendSummary> SummarizeReadyToSendAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentSendSummary(0, 0m));

        public Task<DocumentSendActionResult> SendAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentSendActionResult.Ok("Envoi groupé déclenché."));

        public Task<DocumentSendActionResult> TriggerRunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentSendActionResult.Ok("Traitement déclenché."));
    }

    private sealed class NoOpResolutionService : IDocumentResolutionConsoleService
    {
        public Task<DocumentResolutionConsoleStatus> ResolveManuallyAsync(
            Guid documentId, string? reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentResolutionConsoleStatus.Succeeded);

        public Task<DocumentResolutionConsoleStatus> SupersedeAsync(
            Guid documentId, Guid replacementDocumentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentResolutionConsoleStatus.Succeeded);

        public Task<IReadOnlyList<DocumentReplacementCandidate>> SearchReplacementCandidatesAsync(
            Guid rejectedDocumentId, string? search, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DocumentReplacementCandidate>>(Array.Empty<DocumentReplacementCandidate>());
    }
}

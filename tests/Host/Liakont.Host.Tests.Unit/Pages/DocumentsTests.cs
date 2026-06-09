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
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

public sealed class DocumentsTests : BunitContext
{
    public DocumentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();

        // Services réels du design-system (DeclaredListPage : toast, onglets, sélection persistante,
        // registre de templates) via l'extension supportée. Les deux services non couverts (localisation,
        // contexte acteur) sont stubbés.
        Services.AddCommonUI();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubStringLocalizer());
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor());

        // Préférences de grille et filtres enregistrés : persistés en base en production ; no-op en test
        // (la grille retombe sur les colonnes par défaut du registre et une liste de filtres vide).
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPreferenceService());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilterService());

        // Actions d'envoi (WEB05) : par défaut SANS la permission d'action (lecture seule), service factice.
        // Les tests qui exercent l'envoi RÉ-ENREGISTRENT IPermissionService (canAct: true) et un faux configuré.
        Services.AddScoped<IDocumentSendActions>(_ => new FakeSendActions());
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: false));
    }

    [Fact]
    public void Should_Render_Filters_And_Counts_And_Mask_Send_Actions_For_Readers()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued"),
            Doc("2019", "invoice", "Blocked"),
            Doc("2020", "credit_note", "Detected")));

        var cut = Render<Documents>();

        // Barre de filtres métier (F10 §2.1).
        cut.FindAll("[data-testid='documents-filters']").Should().ContainSingle();
        cut.FindAll("[data-testid='documents-filter-from']").Should().ContainSingle();
        cut.FindAll("[data-testid='documents-filter-state']").Should().ContainSingle();
        cut.FindAll("[data-testid='documents-filter-type']").Should().ContainSingle();

        // Synthèse par état : 2 factures + 1 avoir → Issued=1, Blocked=1, Detected=1, total 3.
        cut.Find("[data-testid='doc-counts-all']").TextContent.Should().Contain("3");
        cut.Find("[data-testid='doc-counts-Issued']").TextContent.Should().Contain("1");
        cut.Find("[data-testid='doc-counts-Blocked']").TextContent.Should().Contain("1");

        // Sans liakont.actions (lecture seule, défaut du contexte), AUCUNE action d'envoi (WEB05 : masquées).
        cut.FindAll("[data-testid='documents-send-all']").Should().BeEmpty();
        cut.FindAll("[data-testid='documents-trigger-run']").Should().BeEmpty();

        cut.FindAll("[data-testid='documents-error']").Should().BeEmpty();
    }

    [Fact]
    public void An_Operator_Sees_The_Send_Actions_In_The_Toolbar()
    {
        var cut = RenderAsOperator(new FakeSendActions(), Doc("2018", "invoice", "ReadyToSend"));

        cut.FindAll("[data-testid='documents-send-all']").Should().ContainSingle("l'opérateur voit « Tout envoyer »");
        cut.FindAll("[data-testid='documents-trigger-run']").Should().ContainSingle("l'opérateur voit « Lancer un traitement »");
    }

    [Fact]
    public void Tout_Envoyer_Shows_The_Confirmation_With_Count_And_Total_Then_Triggers_The_Send()
    {
        var send = new FakeSendActions
        {
            Summary = new DocumentSendSummary(2, 162.80m),
            SendAllResult = DocumentSendActionResult.Ok("Envoi groupé déclenché : 2 document(s)."),
        };
        var cut = RenderAsOperator(send, Doc("2018", "invoice", "ReadyToSend"));

        cut.Find("[data-testid='documents-send-all']").Click();

        // Confirmation AVANT l'envoi : nombre + montant total + mention irréversible (F10).
        var confirmText = cut.Find("[data-testid='documents-send-all-confirm-text']").TextContent;
        confirmText.Should().Contain("2").And.Contain("162,80").And.Contain("IRRÉVERSIBLE");
        send.SendAllCalls.Should().Be(0, "rien n'est envoyé tant que l'opérateur n'a pas confirmé");

        cut.Find("[data-testid='documents-send-all-confirm-button']").Click();

        send.SendAllCalls.Should().Be(1);
        cut.FindAll("[data-testid='documents-send-all-confirm']").Should().BeEmpty("la confirmation se ferme après l'envoi");
        cut.Find("[data-testid='documents-send-feedback']").TextContent.Should().Contain("Envoi groupé déclenché");
    }

    [Fact]
    public void Tout_Envoyer_Cancel_Does_Not_Trigger_Any_Send()
    {
        var send = new FakeSendActions { Summary = new DocumentSendSummary(2, 162.80m) };
        var cut = RenderAsOperator(send, Doc("2018", "invoice", "ReadyToSend"));

        cut.Find("[data-testid='documents-send-all']").Click();
        cut.Find("[data-testid='documents-send-all-cancel']").Click();

        send.SendAllCalls.Should().Be(0);
        cut.FindAll("[data-testid='documents-send-all-confirm']").Should().BeEmpty();
        cut.FindAll("[data-testid='documents-send-feedback']").Should().BeEmpty("aucune action, aucun retour");
    }

    [Fact]
    public void Lancer_Un_Traitement_Calls_The_Service_And_Shows_Feedback()
    {
        var send = new FakeSendActions { TriggerRunResult = DocumentSendActionResult.Ok("Traitement déclenché.") };
        var cut = RenderAsOperator(send, Doc("2018", "invoice", "ReadyToSend"));

        cut.Find("[data-testid='documents-trigger-run']").Click();

        send.TriggerRunCalls.Should().Be(1);
        cut.Find("[data-testid='documents-send-feedback']").TextContent.Should().Contain("Traitement déclenché");
    }

    [Fact]
    public void A_Failed_Send_Surfaces_An_Error_Feedback()
    {
        var send = new FakeSendActions
        {
            Summary = new DocumentSendSummary(1, 100m),
            SendAllResult = DocumentSendActionResult.Failure("L'envoi a échoué : tenant non résolu."),
        };
        var cut = RenderAsOperator(send, Doc("2018", "invoice", "ReadyToSend"));

        cut.Find("[data-testid='documents-send-all']").Click();
        cut.Find("[data-testid='documents-send-all-confirm-button']").Click();

        var feedback = cut.Find("[data-testid='documents-send-feedback']");
        feedback.TextContent.Should().Contain("échoué");
        feedback.GetAttribute("class").Should().Contain("doc-send-feedback--error", "un refus est signalé comme une erreur");
    }

    private IRenderedComponent<Documents> RenderAsOperator(FakeSendActions send, params DocumentSummaryDto[] docs)
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(docs));
        Services.AddScoped<IDocumentSendActions>(_ => send);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        return Render<Documents>();
    }

    [Fact]
    public void Should_Render_Document_Rows_With_State_Badge()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued")));

        var cut = Render<Documents>();

        // La ligne a traversé LoadItems → la grille → le ColumnTemplate d'état (badge FR).
        cut.FindAll("[data-testid='doc-state-Issued']").Should().NotBeEmpty();
    }

    [Fact]
    public void Should_Show_Error_Banner_When_Load_Throws()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Throwing());

        var cut = Render<Documents>();

        // L'échec de chargement reste VISIBLE (bandeau) et n'expose pas la liste (anti faux-vert).
        cut.FindAll("[data-testid='documents-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='documents-filters']").Should().BeEmpty();
    }

    [Fact]
    public void Selecting_A_State_Should_Filter_The_Rendered_Rows()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued", customer: "ALICE"),
            Doc("2019", "invoice", "Blocked", customer: "BOBBY")));

        var cut = Render<Documents>();
        cut.Markup.Should().Contain("ALICE").And.Contain("BOBBY");

        cut.Find("[data-testid='documents-filter-state']").Change("Blocked");

        // Seule la ligne Bloqué reste dans la grille (le filtre client de DeclaredListPage s'applique).
        cut.Markup.Should().Contain("BOBBY");
        cut.Markup.Should().NotContain("ALICE");
    }

    [Fact]
    public void Selecting_A_Type_Should_Update_The_Counts_And_The_Rows()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued", customer: "ALICE"),
            Doc("2020", "credit_note", "Blocked", customer: "CAROLE")));

        var cut = Render<Documents>();

        cut.Find("[data-testid='documents-filter-type']").Change("Avoir");

        // Les compteurs honorent le type : seul l'avoir (Bloqué) est compté.
        cut.Find("[data-testid='doc-counts-Blocked']").TextContent.Should().Contain("1");
        cut.Find("[data-testid='doc-counts-Issued']").TextContent.Should().Contain("0");
        cut.Find("[data-testid='doc-counts-all']").TextContent.Should().Contain("1");

        // Les lignes sont filtrées : seule la facture (ALICE) disparaît.
        cut.Markup.Should().Contain("CAROLE");
        cut.Markup.Should().NotContain("ALICE");
    }

    [Fact]
    public void Clicking_A_Count_Chip_Should_Filter_The_List_By_That_State()
    {
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Returning(
            Doc("2018", "invoice", "Issued", customer: "ALICE"),
            Doc("2019", "invoice", "Blocked", customer: "BOBBY")));

        var cut = Render<Documents>();

        cut.Find("[data-testid='doc-counts-Blocked']").Click();

        cut.Markup.Should().Contain("BOBBY");
        cut.Markup.Should().NotContain("ALICE");
    }

    [Fact]
    public void Changing_Period_To_A_Scope_Without_The_Selected_Type_Should_Reset_It()
    {
        // 1er périmètre : une facture (type « Facture » disponible). 2e périmètre : un avoir seulement.
        Services.AddScoped<IDocumentConsoleQueries>(_ => FakeDocumentConsoleQueries.Switching(
            [Doc("2018", "invoice", "Issued", customer: "ALICE")],
            [Doc("2030", "credit_note", "Blocked", customer: "CAROLE")]));

        var cut = Render<Documents>();
        cut.Find("[data-testid='documents-filter-type']").Change("Facture");

        // Changement de période → rechargement d'un périmètre où « Facture » n'existe plus → retour à Tous.
        cut.Find("[data-testid='documents-filter-from']").Change("2026-05-01");

        var typeSelect = cut.Find("[data-testid='documents-filter-type']");
        typeSelect.GetAttribute("value").Should().BeNullOrEmpty("le type sélectionné disparu est réinitialisé à « Tous »");
        cut.Markup.Should().NotContain(">Facture<", "l'option Facture n'existe plus dans ce périmètre");
        cut.Markup.Should().Contain("CAROLE");
    }

    private static DocumentSummaryDto Doc(string number, string type, string state, string customer = "DUPONT J.") => new()
    {
        Id = Guid.NewGuid(),
        DocumentNumber = number,
        DocumentType = type,
        IssueDate = new DateOnly(2026, 6, 1),
        CustomerName = customer,
        TotalGross = 1162.80m,
        State = state,
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeDocumentConsoleQueries : IDocumentConsoleQueries
    {
        private readonly IReadOnlyList<IReadOnlyList<DocumentSummaryDto>>? _scopes;
        private readonly bool _throws;
        private int _call;

        private FakeDocumentConsoleQueries(IReadOnlyList<IReadOnlyList<DocumentSummaryDto>>? scopes, bool throws)
        {
            _scopes = scopes;
            _throws = throws;
        }

        public static FakeDocumentConsoleQueries Returning(params DocumentSummaryDto[] documents) => new([documents], throws: false);

        // Renvoie un périmètre différent à chaque rechargement (1er appel = premier jeu, etc. ; le dernier
        // se répète) — pour tester le changement de période.
        public static FakeDocumentConsoleQueries Switching(params DocumentSummaryDto[][] scopes) => new(scopes, throws: false);

        public static FakeDocumentConsoleQueries Throwing() => new(null, throws: true);

        public Task<IReadOnlyList<DocumentSummaryDto>> GetDocumentsInPeriodAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement des documents.");
            }

            var index = Math.Min(_call, _scopes!.Count - 1);
            _call++;
            return Task.FromResult(_scopes[index]);
        }
    }

    private sealed class FakeSendActions : IDocumentSendActions
    {
        public DocumentSendSummary Summary { get; set; } = new(0, 0m);

        public DocumentSendActionResult SendAllResult { get; set; } = DocumentSendActionResult.Ok("Envoi groupé déclenché.");

        public DocumentSendActionResult TriggerRunResult { get; set; } = DocumentSendActionResult.Ok("Traitement déclenché.");

        public DocumentSendActionResult SendSelectionResult { get; set; } = DocumentSendActionResult.Ok("Envoi de la sélection déclenché.");

        public int SendAllCalls { get; private set; }

        public int TriggerRunCalls { get; private set; }

        public IReadOnlyList<Guid>? LastSelection { get; private set; }

        public Task<DocumentSendActionResult> SendSelectionAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken = default)
        {
            LastSelection = documentIds.ToList();
            return Task.FromResult(SendSelectionResult);
        }

        public Task<DocumentSendSummary> SummarizeReadyToSendAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Summary);

        public Task<DocumentSendActionResult> SendAllAsync(CancellationToken cancellationToken = default)
        {
            SendAllCalls++;
            return Task.FromResult(SendAllResult);
        }

        public Task<DocumentSendActionResult> TriggerRunAsync(CancellationToken cancellationToken = default)
        {
            TriggerRunCalls++;
            return Task.FromResult(TriggerRunResult);
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

    private sealed class NullSavedFilterService : ISavedFilterService
    {
        public Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SavedFilter>>([]);

        public Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SavedFilter?>(null);

        public Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default) =>
            Task.FromResult(filter);

        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetDefaultAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullGridPreferenceService : IGridPreferenceService
    {
        public Task<UserGridPreference?> GetPreferenceAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<UserGridPreference?>(null);

        public Task SavePreferenceAsync(Guid userId, string gridKey, IReadOnlyList<string> columnKeys, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveViewPreferenceAsync(Guid userId, string gridKey, ViewKind viewKind, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveFilterStateAsync(Guid userId, string gridKey, string? filterStateJson, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveColumnWidthsAsync(Guid userId, string gridKey, IReadOnlyDictionary<string, string> columnWidths, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubStringLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class StubActorContextAccessor : IActorContextAccessor
    {
        public IActorContext Current { get; } = new StubActorContext();

        private sealed class StubActorContext : IActorContext
        {
            public Guid UserId => Guid.Empty;

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated => true;

            public string? DisplayName => "Test";

            public string? Email => null;

            public Guid? CompanyId => null;

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-test";
        }
    }
}

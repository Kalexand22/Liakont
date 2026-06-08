namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
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
    }

    [Fact]
    public void Should_Render_Filters_Counts_And_Disabled_Send_Actions()
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

        // Actions d'envoi présentes mais désactivées (branchées en WEB05).
        cut.Find("[data-testid='documents-send-selection']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='documents-send-all']").HasAttribute("disabled").Should().BeTrue();

        cut.FindAll("[data-testid='documents-error']").Should().BeEmpty();
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

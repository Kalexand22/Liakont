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

    private static DocumentSummaryDto Doc(string number, string type, string state) => new()
    {
        Id = Guid.NewGuid(),
        DocumentNumber = number,
        DocumentType = type,
        IssueDate = new DateOnly(2026, 6, 1),
        CustomerName = "DUPONT J.",
        TotalGross = 1162.80m,
        State = state,
        LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeDocumentConsoleQueries : IDocumentConsoleQueries
    {
        private readonly IReadOnlyList<DocumentSummaryDto>? _documents;
        private readonly bool _throws;

        private FakeDocumentConsoleQueries(IReadOnlyList<DocumentSummaryDto>? documents, bool throws)
        {
            _documents = documents;
            _throws = throws;
        }

        public static FakeDocumentConsoleQueries Returning(params DocumentSummaryDto[] documents) => new(documents, throws: false);

        public static FakeDocumentConsoleQueries Throwing() => new(null, throws: true);

        public Task<IReadOnlyList<DocumentSummaryDto>> GetDocumentsInPeriodAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement des documents.");
            }

            return Task.FromResult(_documents!);
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

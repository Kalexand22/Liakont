namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Payments;
using Liakont.Modules.Pipeline.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

public sealed class EncaissementsTests : BunitContext
{
    public EncaissementsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();

        // Services réels du design-system (DeclaredListPage : toast, onglets, sélection persistante,
        // registre de templates). Localisation + contexte acteur stubbés ; préférences/filtres no-op.
        Services.AddCommonUI();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubStringLocalizer());
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor());
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPreferenceService());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilterService());
    }

    [Fact]
    public void Renders_Aggregates_With_Status_Badge_And_No_Banner_When_Supported_And_Fiscal_Decided()
    {
        Services.AddScoped<IEncaissementsConsoleQueries>(_ => FakeEncaissementsQueries.Returning(
            Model([Agg("Calculated")], fiscalPending: false, paymentSupported: true)));

        var cut = Render<Encaissements>();

        cut.Find("h1").TextContent.Should().Contain("Encaissements");

        // État 1 (F10 §2.4) : PA capable + fiscal renseigné → agrégats avec badge, AUCUN bandeau.
        cut.FindAll("[data-testid='encaissements-fiscal-pending']").Should().BeEmpty();
        cut.FindAll("[data-testid='encaissements-capability-pending']").Should().BeEmpty();
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='payment-status-Calculated']").Should().NotBeEmpty());
    }

    [Fact]
    public void Shows_Capability_Banner_When_The_Pa_Does_Not_Support_Payments()
    {
        Services.AddScoped<IEncaissementsConsoleQueries>(_ => FakeEncaissementsQueries.Returning(
            Model(
                [Agg("PendingCapability", reason: "La plateforme ne déclare pas la transmission des paiements.")],
                fiscalPending: false,
                paymentSupported: false,
                paName: "B2Brouter")));

        var cut = Render<Encaissements>();

        // État 2 (F10 §2.4) : PA sans capacité → bandeau nommant la plateforme, pas de bandeau fiscal.
        var banner = cut.Find("[data-testid='encaissements-capability-pending']");
        banner.TextContent.Should().Contain("B2Brouter");
        banner.TextContent.Should().Contain("ne supporte pas encore");
        cut.FindAll("[data-testid='encaissements-fiscal-pending']").Should().BeEmpty();
    }

    [Fact]
    public void Shows_Fiscal_Pending_Banner_When_A_Fiscal_Decision_Is_Missing()
    {
        Services.AddScoped<IEncaissementsConsoleQueries>(_ => FakeEncaissementsQueries.Returning(
            Model([Agg("Suspended", reason: "Catégorie d'opération non renseignée.")], fiscalPending: true)));

        var cut = Render<Encaissements>();

        // État 3 (F10 §2.4) : décision fiscale en attente → bandeau explicite (TVA débits / catégorie).
        var banner = cut.Find("[data-testid='encaissements-fiscal-pending']");
        banner.TextContent.Should().Contain("Décision fiscale en attente");
        banner.TextContent.Should().Contain("expert-comptable");
    }

    [Fact]
    public void Shows_An_Explicit_Empty_State_When_There_Are_No_Aggregates()
    {
        Services.AddScoped<IEncaissementsConsoleQueries>(_ => FakeEncaissementsQueries.Returning(
            Model([])));

        var cut = Render<Encaissements>();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='encaissements-empty']").Should().ContainSingle());
    }

    [Fact]
    public void Shows_Error_Banner_When_Load_Throws()
    {
        Services.AddScoped<IEncaissementsConsoleQueries>(_ => FakeEncaissementsQueries.Throwing());

        var cut = Render<Encaissements>();

        // L'échec de chargement reste VISIBLE (bandeau) et n'expose AUCUNE donnée (ni liste, ni état vide,
        // ni bandeaux), mais le filtre période RESTE disponible pour réessayer (anti faux-vert + ergonomie).
        cut.FindAll("[data-testid='encaissements-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='encaissements-filters']").Should().ContainSingle();
        cut.FindAll("[data-testid='encaissements-empty']").Should().BeEmpty();
        cut.FindAll("[data-testid='encaissements-fiscal-pending']").Should().BeEmpty();
    }

    [Fact]
    public void Formats_Amounts_And_Rate_In_French()
    {
        Services.AddScoped<IEncaissementsConsoleQueries>(_ => FakeEncaissementsQueries.Returning(
            Model([Agg("Calculated", rate: 20m, baseHt: 100.00m, vat: 20.00m)])));

        var cut = Render<Encaissements>();

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            markup.Should().Contain("20 %");      // taux rendu « 20 % »
            markup.Should().Contain("100,00");    // base HT N2 fr-FR
            markup.Should().Contain("20,00");     // TVA N2 fr-FR
        });
    }

    [Fact]
    public void Changing_The_Period_Reloads_The_Aggregates_For_That_Month()
    {
        var spy = new SpyEncaissementsQueries(
            Model([Agg("Calculated")]),
            Model([Agg("Suspended", reason: "Catégorie d'opération non renseignée.")], fiscalPending: true));
        Services.AddScoped<IEncaissementsConsoleQueries>(_ => spy);

        var cut = Render<Encaissements>();

        // 1er chargement : l'agrégat Calculated du mois courant, aucun bandeau fiscal.
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='payment-status-Calculated']").Should().NotBeEmpty());

        // Changement de période → nouvelle requête avec ce mois + rechargement de la liste.
        cut.Find("[data-testid='encaissements-filter-period']").Change("2026-01");

        cut.WaitForAssertion(() =>
        {
            spy.RequestedPeriods.Should().Contain("2026-01");
            cut.FindAll("[data-testid='payment-status-Suspended']").Should().NotBeEmpty();
            cut.FindAll("[data-testid='payment-status-Calculated']").Should().BeEmpty();
            cut.FindAll("[data-testid='encaissements-fiscal-pending']").Should().ContainSingle();
        });
    }

    private static EncaissementsViewModel Model(
        IReadOnlyList<PaymentAggregateRow> aggregates,
        bool fiscalPending = false,
        bool paymentSupported = true,
        bool hasPa = true,
        string? paName = "B2Brouter") => new()
    {
        Aggregates = aggregates,
        FiscalDecisionPending = fiscalPending,
        PaymentReportingSupported = paymentSupported,
        HasConfiguredPa = hasPa,
        PaName = paName,
    };

    private static PaymentAggregateRow Agg(
        string status,
        decimal rate = 20m,
        decimal baseHt = 100.00m,
        decimal vat = 20.00m,
        string? reason = null) =>
        PaymentAggregateRow.FromDto(new PaymentDailyAggregateDto
        {
            Id = Guid.NewGuid(),
            AggregateDate = new DateOnly(2026, 6, 1),
            VatRate = rate,
            TaxableBase = baseHt,
            VatAmount = vat,
            Status = status,
            Reason = reason,
            ComputedUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        });

    private sealed class FakeEncaissementsQueries : IEncaissementsConsoleQueries
    {
        private readonly EncaissementsViewModel? _model;
        private readonly bool _throws;

        private FakeEncaissementsQueries(EncaissementsViewModel? model, bool throws)
        {
            _model = model;
            _throws = throws;
        }

        public static FakeEncaissementsQueries Returning(EncaissementsViewModel model) => new(model, throws: false);

        public static FakeEncaissementsQueries Throwing() => new(null, throws: true);

        public Task<EncaissementsViewModel> GetEncaissementsAsync(string? period, CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement des encaissements.");
            }

            return Task.FromResult(_model!);
        }
    }

    private sealed class SpyEncaissementsQueries : IEncaissementsConsoleQueries
    {
        private readonly EncaissementsViewModel _firstLoad;
        private readonly EncaissementsViewModel _afterPeriodChange;
        private int _calls;

        public SpyEncaissementsQueries(
            EncaissementsViewModel firstLoad,
            EncaissementsViewModel afterPeriodChange)
        {
            _firstLoad = firstLoad;
            _afterPeriodChange = afterPeriodChange;
        }

        public List<string?> RequestedPeriods { get; } = [];

        public Task<EncaissementsViewModel> GetEncaissementsAsync(
            string? period,
            CancellationToken cancellationToken = default)
        {
            RequestedPeriods.Add(period);
            var model = _calls == 0 ? _firstLoad : _afterPeriodChange;
            _calls++;
            return Task.FromResult(model);
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

namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.TvaMappingTable;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit de la PAGE « Paramétrage comptable — Table TVA » (WEB07a) : câblage chargement →
/// échec visible, gating du bouton de validation par <c>liakont.settings</c>, et parcours de
/// validation (confirmation par ressaisie → la table passe Validée, le bandeau NON VALIDÉE est levé).
/// Le rendu détaillé est couvert par <c>TableTvaViewTests</c> ; ici on prouve le wiring page ↔ service.
/// </summary>
public sealed class TableTvaTests : BunitContext
{
    public TableTvaTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddLogging();
        Services.AddLocalization();

        // Graphe Common.UI réel (la page rend TableTvaView → DeclaredListPage). Acteur anonyme +
        // services de préférences no-op : aucun accès base pour le rendu.
        Services.AddCommonUI();
        Services.AddSingleton<IActorContextAccessor>(new FakeActorContextAccessor());
        Services.AddScoped<IGridPreferenceService, NoOpGridPreferenceService>();
        Services.AddScoped<ISavedFilterService, NoOpSavedFilterService>();
    }

    [Fact]
    public void Load_failure_shows_a_visible_error_banner()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<ITvaMappingTableQueries>(_ => FakeTableQueries.Throwing());

        var cut = Render<TableTva>();

        // L'échec de chargement reste VISIBLE (bandeau) et n'expose pas la vue (anti faux-vert).
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='table-tva-error']").Should().ContainSingle());
        cut.FindAll("[data-testid='liakont-tva-table']").Should().BeEmpty();
    }

    [Fact]
    public void Without_settings_permission_the_validate_button_is_hidden()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: false));
        Services.AddScoped<ITvaMappingTableQueries>(_ => FakeTableQueries.NotValidated());

        var cut = Render<TableTva>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='table-tva-not-validated']").Should().ContainSingle());
        cut.FindAll("[data-testid='table-tva-validate-btn']").Should().BeEmpty();
    }

    [Fact]
    public void With_settings_permission_the_validate_button_is_shown()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<ITvaMappingTableQueries>(_ => FakeTableQueries.NotValidated());

        var cut = Render<TableTva>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='table-tva-validate-btn']").Should().ContainSingle());
    }

    [Fact]
    public void Validating_marks_the_table_validated_and_lifts_the_banner()
    {
        var fake = FakeTableQueries.NotValidated("Alice Martin");
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<ITvaMappingTableQueries>(_ => fake);

        var cut = Render<TableTva>();

        // Table NON VALIDÉE au départ.
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='table-tva-validate-btn']").Should().ContainSingle());

        // Ouvre la confirmation, ressaisit le nom du validateur, confirme.
        cut.Find("[data-testid='table-tva-validate-btn']").Click();
        cut.Find("[data-testid='table-tva-validator-input']").Input("Alice Martin");
        cut.Find("[data-testid='table-tva-confirm-btn']").Click();

        // La commande de validation a été émise et la table rechargée passe Validée (bandeau levé).
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='table-tva-validated']").Should().ContainSingle();
            cut.FindAll("[data-testid='table-tva-not-validated']").Should().BeEmpty();
        });
        fake.ValidateCalls.Should().Be(1);
    }

    private sealed class FakeTableQueries : ITvaMappingTableQueries
    {
        private readonly bool _throwOnLoad;
        private readonly string _operator;
        private bool _validated;

        private FakeTableQueries(bool throwOnLoad, string op)
        {
            _throwOnLoad = throwOnLoad;
            _operator = op;
        }

        public int ValidateCalls { get; private set; }

        public static FakeTableQueries NotValidated(string op = "Alice Martin") => new(throwOnLoad: false, op);

        public static FakeTableQueries Throwing() => new(throwOnLoad: true, "Alice Martin");

        public Task<TvaMappingTableViewModel> GetTableAsync(CancellationToken cancellationToken = default)
        {
            if (_throwOnLoad)
            {
                throw new InvalidOperationException("Échec simulé du chargement de la table TVA.");
            }

            return Task.FromResult(BuildModel());
        }

        public Task ValidateAsync(CancellationToken cancellationToken = default)
        {
            ValidateCalls++;
            _validated = true;
            return Task.CompletedTask;
        }

        private TvaMappingTableViewModel BuildModel() => new()
        {
            Table = new MappingTableDto
            {
                Id = Guid.NewGuid(),
                CompanyId = Guid.NewGuid(),
                MappingVersion = "v1",
                ValidatedBy = _validated ? _operator : null,
                ValidatedDate = _validated ? new DateOnly(2026, 6, 1) : null,
                IsValidated = _validated,
                DefaultBehavior = "Block",
                CreatedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = null,
                Rules =
                [
                    new MappingRuleDto
                    {
                        SourceRegimeCode = "20",
                        Label = "TVA 20 %",
                        Part = "Adjudication",
                        Category = "S",
                        Vatex = null,
                        RateMode = "Fixed",
                        RateValue = 20m,
                    },
                ],
            },
            ChangeLog = Array.Empty<MappingChangeLogEntryDto>(),
            CurrentOperatorName = _operator,
        };
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _hasSettings;

        public FakePermissionService(bool hasSettings) => _hasSettings = hasSettings;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _hasSettings && string.Equals(permission, "liakont.settings", StringComparison.Ordinal);
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public IActorContext Current { get; } = new AnonymousActorContext();

        private sealed class AnonymousActorContext : IActorContext
        {
            public Guid UserId => Guid.Empty;

            public Guid CorrelationId { get; } = Guid.NewGuid();

            public bool IsAuthenticated => false;

            public string? DisplayName => null;

            public string? Email => null;

            public Guid? CompanyId => null;

            public string? Timezone => null;

            public string? Language => null;

            public string? TenantId => null;
        }
    }

    private sealed class NoOpGridPreferenceService : IGridPreferenceService
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

    private sealed class NoOpSavedFilterService : ISavedFilterService
    {
        public Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SavedFilter>>(Array.Empty<SavedFilter>());

        public Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SavedFilter?>(null);

        public Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default) =>
            Task.FromResult(filter);

        public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SetDefaultAsync(Guid id, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

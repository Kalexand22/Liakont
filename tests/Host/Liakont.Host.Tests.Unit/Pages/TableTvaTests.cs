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
using Stratum.Common.Abstractions.Exceptions;
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

    [Fact]
    public void Validation_failure_keeps_the_dialog_open_and_the_table_not_validated()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<ITvaMappingTableQueries>(_ => FakeTableQueries.NotValidatedFailingValidate("Alice Martin"));

        var cut = Render<TableTva>();

        // Bouton de validation visible (table NON VALIDÉE).
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='table-tva-validate-btn']").Should().ContainSingle());

        // Ouvre la confirmation, ressaisit le nom du validateur, confirme.
        cut.Find("[data-testid='table-tva-validate-btn']").Click();
        cut.Find("[data-testid='table-tva-validator-input']").Input("Alice Martin");
        cut.Find("[data-testid='table-tva-confirm-btn']").Click();

        // Après échec : le message d'erreur est affiché, le dialogue reste ouvert, la table reste NON VALIDÉE.
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='table-tva-validate-error']").Should().ContainSingle();
            cut.FindAll("[data-testid='table-tva-confirm']").Should().ContainSingle();
            cut.FindAll("[data-testid='table-tva-not-validated']").Should().ContainSingle();
            cut.FindAll("[data-testid='table-tva-validated']").Should().BeEmpty();
        });
    }

    [Fact]
    public void Creating_a_rule_via_the_editor_calls_add_and_marks_the_table_not_validated()
    {
        var fake = FakeTableQueries.Validated("Alice Martin");
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<ITvaMappingTableQueries>(_ => fake);

        var cut = Render<TableTva>();

        // Table VALIDÉE au départ ; le bouton de création est disponible (permission settings).
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='table-tva-create-btn']").Should().ContainSingle());
        cut.Find("[data-testid='table-tva-create-btn']").Click();

        // Saisie via les listes FERMÉES + code régime.
        cut.Find("[data-testid='tva-rule-code']").Input("6");
        cut.Find("[data-testid='tva-rule-part']").Change("Adjudication");
        cut.Find("[data-testid='tva-rule-category']").Change("S");
        cut.Find("[data-testid='tva-rule-ratemode']").Change("Fixed");
        cut.Find("[data-testid='tva-rule-rate']").Change("20");
        cut.Find("[data-testid='tva-rule-save-btn']").Click();

        // La commande d'ajout a été émise et le rechargement repasse la table « NON VALIDÉE ».
        cut.WaitForAssertion(() =>
        {
            fake.AddCalls.Should().Be(1);
            cut.FindAll("[data-testid='table-tva-not-validated']").Should().ContainSingle();
            cut.FindAll("[data-testid='tva-rule-editor']").Should().BeEmpty();
        });
    }

    [Fact]
    public void Editing_a_rule_via_the_editor_calls_update_and_marks_the_table_not_validated()
    {
        var fake = FakeTableQueries.Validated("Alice Martin");
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<ITvaMappingTableQueries>(_ => fake);

        var cut = Render<TableTva>();

        // Quick-action « Modifier » sur une règle existante → ouverture de l'éditeur (clé figée, valeurs pré-remplies).
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='quick-action-edit']").Should().NotBeEmpty());
        cut.FindAll("[data-testid='quick-action-edit']")[0].Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='tva-rule-editor']").Should().ContainSingle());

        // La règle pré-remplie est déjà valide (clé + catégorie + mode + taux) : enregistrer émet la mise à jour.
        cut.Find("[data-testid='tva-rule-save-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            fake.UpdateCalls.Should().Be(1);
            cut.FindAll("[data-testid='table-tva-not-validated']").Should().ContainSingle();
            cut.FindAll("[data-testid='tva-rule-editor']").Should().BeEmpty();
        });
    }

    [Fact]
    public void Coverage_create_button_opens_the_editor_prefilled_with_the_regime_code()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<ITvaMappingTableQueries>(_ => FakeTableQueries.NotValidated());

        var cut = Render<TableTva>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='table-tva-coverage-create-99']").Should().ContainSingle());
        cut.Find("[data-testid='table-tva-coverage-create-99']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='tva-rule-editor']").Should().ContainSingle();
            cut.Find("[data-testid='tva-rule-code']").GetAttribute("value").Should().Be("99");
        });
    }

    [Fact]
    public void Deleting_a_rule_via_quick_action_calls_remove()
    {
        var fake = FakeTableQueries.NotValidated();
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<ITvaMappingTableQueries>(_ => fake);

        var cut = Render<TableTva>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='quick-action-delete']").Should().NotBeEmpty());
        cut.FindAll("[data-testid='quick-action-delete']")[0].Click();

        // Confirmation inline, puis suppression effective.
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='table-tva-delete-confirm']").Should().ContainSingle());
        cut.Find("[data-testid='table-tva-delete-confirm-btn']").Click();

        cut.WaitForAssertion(() => fake.RemoveCalls.Should().Be(1));
    }

    [Fact]
    public void Mutation_failure_shows_the_business_error_in_the_editor()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<ITvaMappingTableQueries>(_ => FakeTableQueries.FailingMutate());

        var cut = Render<TableTva>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='table-tva-create-btn']").Should().ContainSingle());
        cut.Find("[data-testid='table-tva-create-btn']").Click();

        cut.Find("[data-testid='tva-rule-code']").Input("6");
        cut.Find("[data-testid='tva-rule-part']").Change("Adjudication");
        cut.Find("[data-testid='tva-rule-category']").Change("E");
        cut.Find("[data-testid='tva-rule-ratemode']").Change("ComputedFromSource");
        cut.Find("[data-testid='tva-rule-save-btn']").Click();

        // Message métier (français) du handler affiché dans l'éditeur, qui reste ouvert.
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='tva-rule-editor-error']").TextContent.Should().Contain("introuvable");
            cut.FindAll("[data-testid='tva-rule-editor']").Should().ContainSingle();
        });
    }

    private sealed class FakeTableQueries : ITvaMappingTableQueries
    {
        private readonly bool _throwOnLoad;
        private readonly bool _throwOnValidate;
        private readonly bool _throwOnMutate;
        private readonly string _operator;
        private bool _validated;

        private FakeTableQueries(bool throwOnLoad, bool throwOnValidate, bool throwOnMutate, bool validated, string op)
        {
            _throwOnLoad = throwOnLoad;
            _throwOnValidate = throwOnValidate;
            _throwOnMutate = throwOnMutate;
            _validated = validated;
            _operator = op;
        }

        public int ValidateCalls { get; private set; }

        public int AddCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public int RemoveCalls { get; private set; }

        public static FakeTableQueries NotValidated(string op = "Alice Martin") => new(false, false, false, validated: false, op);

        public static FakeTableQueries Validated(string op = "Alice Martin") => new(false, false, false, validated: true, op);

        public static FakeTableQueries Throwing() => new(true, false, false, validated: false, "Alice Martin");

        public static FakeTableQueries NotValidatedFailingValidate(string op = "Alice Martin") => new(false, true, false, validated: false, op);

        public static FakeTableQueries FailingMutate(string op = "Alice Martin") => new(false, false, true, validated: false, op);

        private static TvaMappingEditOptionsDto EditOptions() => new()
        {
            Categories = [new TvaMappingOptionDto("S", "Taux normal"), new TvaMappingOptionDto("E", "Exonéré")],
            Parts = [new TvaMappingOptionDto("Adjudication", "Adjudication"), new TvaMappingOptionDto("Frais", "Frais")],
            RateModes = [new TvaMappingOptionDto("Fixed", "Taux fixe"), new TvaMappingOptionDto("ComputedFromSource", "Calculé")],
            VatexCodes = [new TvaMappingOptionDto("VATEX-EU-J", "VATEX-EU-J — Collection")],
        };

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
            if (_throwOnValidate)
            {
                throw new InvalidOperationException("Échec simulé de la validation.");
            }

            ValidateCalls++;
            _validated = true;
            return Task.CompletedTask;
        }

        public Task AddRuleAsync(TvaRuleFormModel model, CancellationToken cancellationToken = default)
        {
            if (_throwOnMutate)
            {
                // Erreur métier ATTENDUE (DomainException) : message opérateur français affiché tel quel.
                throw new NotFoundException("Table de mapping introuvable pour ce tenant.");
            }

            AddCalls++;
            _validated = false; // toute mutation invalide la table (parité avec le handler).
            return Task.CompletedTask;
        }

        public Task UpdateRuleAsync(TvaRuleFormModel model, CancellationToken cancellationToken = default)
        {
            if (_throwOnMutate)
            {
                throw new NotFoundException("Règle de mapping introuvable.");
            }

            UpdateCalls++;
            _validated = false;
            return Task.CompletedTask;
        }

        public Task RemoveRuleAsync(string sourceRegimeCode, string part, CancellationToken cancellationToken = default)
        {
            if (_throwOnMutate)
            {
                throw new NotFoundException("Règle de mapping introuvable.");
            }

            RemoveCalls++;
            _validated = false;
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
            Coverage = new MappingCoverageReportDto
            {
                IsTableConfigured = true,
                MappingVersion = "v1",
                IsTableValidated = _validated,
                Verdict = "Incomplete",
                CoveredRegimes = Array.Empty<RegimeCoverageDto>(),
                AbsentRegimes =
                [
                    new RegimeCoverageDto
                    {
                        Code = "99",
                        Label = "Régime exotique",
                        Occurrences = 7,
                        LastSeenAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    },
                ],
            },
            EditOptions = EditOptions(),
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

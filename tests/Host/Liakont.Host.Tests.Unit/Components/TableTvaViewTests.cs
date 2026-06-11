namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.TvaMappingTable;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit du rendu PUR de la page « Paramétrage comptable — Table TVA » (WEB07a) : état de
/// validation (✅ / ⚠️ + bandeau permanent), table des règles (gabarit DeclaredListPage), changelog,
/// visibilité du bouton de validation selon la permission, et garde de confirmation « ressaisir son
/// nom ». La vue ne contient aucune logique fiscale : elle reçoit son modèle et ses callbacks.
/// </summary>
public sealed class TableTvaViewTests : BunitContext
{
    public TableTvaViewTests()
    {
        // Radzen / grille s'appuient sur le JS interop — loose mode capte tous les appels.
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddLogging();
        Services.AddLocalization();

        // Graphe Common.UI réel (DeclaredListPage → StratumDataGrid) ; acteur anonyme (UserId == Empty)
        // pour court-circuiter les lectures de préférences par utilisateur — aucune base requise.
        Services.AddCommonUI();
        Services.AddSingleton<IActorContextAccessor>(new FakeActorContextAccessor());
        Services.AddScoped<IGridPreferenceService, NoOpGridPreferenceService>();
        Services.AddScoped<ISavedFilterService, NoOpSavedFilterService>();
    }

    [Fact]
    public void Validated_table_shows_validated_badge_and_no_banner()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(ValidatedTable()))
            .Add(v => v.CanValidate, true));

        cut.FindAll("[data-testid='table-tva-validated']").Should().ContainSingle();
        cut.Markup.Should().Contain("Validée par Alice Martin");
        cut.FindAll("[data-testid='table-tva-not-validated']").Should().BeEmpty();

        // Une table déjà validée n'offre pas le bouton de validation (rien à revalider).
        cut.FindAll("[data-testid='table-tva-validate-btn']").Should().BeEmpty();
    }

    [Fact]
    public void Not_validated_table_shows_permanent_banner_and_hides_button_without_permission()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable()))
            .Add(v => v.CanValidate, false));

        cut.FindAll("[data-testid='table-tva-not-validated']").Should().ContainSingle();
        cut.Markup.Should().Contain("Les envois sont suspendus");

        // Sans liakont.settings, pas de bouton de validation.
        cut.FindAll("[data-testid='table-tva-validate-btn']").Should().BeEmpty();
    }

    [Fact]
    public void Not_validated_table_with_permission_shows_validate_button()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable()))
            .Add(v => v.CanValidate, true));

        cut.FindAll("[data-testid='table-tva-validate-btn']").Should().ContainSingle();
    }

    [Fact]
    public void No_table_shows_explicit_empty_state()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(null))
            .Add(v => v.CanValidate, true));

        cut.FindAll("[data-testid='table-tva-none']").Should().ContainSingle();

        // Pas de section règles ni de bouton de validation sans table.
        cut.FindAll("[data-testid='table-tva-rules']").Should().BeEmpty();
        cut.FindAll("[data-testid='table-tva-validate-btn']").Should().BeEmpty();
    }

    [Fact]
    public void Rules_are_rendered_with_category_badge_and_rate_modes()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable()))
            .Add(v => v.CanValidate, false));

        var markup = cut.Markup;

        // Régime, catégorie et taux fixe formaté à la française.
        markup.Should().Contain("20");
        markup.Should().Contain("20 %");

        // Mode « calculé depuis la source » : pas de taux deviné.
        markup.Should().Contain("Calculé (source)");

        // Catégorie affichée en badge.
        cut.FindAll("[data-testid='category-badge']").Should().NotBeEmpty();
    }

    [Fact]
    public void Changelog_entries_are_rendered_in_french()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), WithChangeLog()))
            .Add(v => v.CanValidate, false));

        cut.FindAll("[data-testid='table-tva-changelog-entry']").Should().HaveCount(2);
        cut.Markup.Should().Contain("Table validée");
        cut.Markup.Should().Contain("Règle ajoutée");
        cut.Markup.Should().Contain("par Alice Martin");
    }

    [Fact]
    public void Confirm_button_is_disabled_until_operator_name_is_retyped()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable()))
            .Add(v => v.CanValidate, true)
            .Add(v => v.ConfirmOpen, true));

        // Dialogue ouvert : opérateur attendu affiché, bouton Confirmer désactivé tant qu'on n'a pas ressaisi.
        cut.FindAll("[data-testid='table-tva-confirm']").Should().ContainSingle();
        cut.Find("[data-testid='table-tva-confirm-operator']").TextContent.Should().Contain("Alice Martin");
        cut.Find("[data-testid='table-tva-confirm-btn']").HasAttribute("disabled").Should().BeTrue();

        // Mauvais nom → toujours désactivé.
        cut.Find("[data-testid='table-tva-validator-input']").Input("Bob");
        cut.Find("[data-testid='table-tva-confirm-btn']").HasAttribute("disabled").Should().BeTrue();

        // Nom exact (insensible à la casse / espaces) → activé.
        cut.Find("[data-testid='table-tva-validator-input']").Input("  alice martin ");
        cut.Find("[data-testid='table-tva-confirm-btn']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Confirming_invokes_the_validate_callback()
    {
        var confirmed = false;
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable()))
            .Add(v => v.CanValidate, true)
            .Add(v => v.ConfirmOpen, true)
            .Add(v => v.OnConfirmValidate, () => { confirmed = true; }));

        cut.Find("[data-testid='table-tva-validator-input']").Input("Alice Martin");
        cut.Find("[data-testid='table-tva-confirm-btn']").Click();

        confirmed.Should().BeTrue();
    }

    [Fact]
    public void Opening_confirm_invokes_the_open_callback()
    {
        var opened = false;
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable()))
            .Add(v => v.CanValidate, true)
            .Add(v => v.OnOpenConfirm, () => { opened = true; }));

        cut.Find("[data-testid='table-tva-validate-btn']").Click();

        opened.Should().BeTrue();
    }

    [Fact]
    public void Validation_error_is_displayed_in_the_confirm_dialog()
    {
        const string ErrorMessage = "La validation a échoué. Réessayez plus tard.";

        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable()))
            .Add(v => v.CanValidate, true)
            .Add(v => v.ConfirmOpen, true)
            .Add(v => v.ValidateError, ErrorMessage));

        // Le dialogue est toujours ouvert.
        cut.FindAll("[data-testid='table-tva-confirm']").Should().ContainSingle();

        // L'alerte d'erreur est présente et contient le message.
        cut.FindAll("[data-testid='table-tva-validate-error']").Should().ContainSingle();
        cut.Find("[data-testid='table-tva-validate-error']").TextContent.Should().Contain(ErrorMessage);
    }

    [Fact]
    public void Edit_controls_are_hidden_without_settings_permission()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), coverage: CoverageWithAbsent()))
            .Add(v => v.CanEdit, false));

        // Aucun bouton de création, aucune quick-action d'édition / suppression sans la permission.
        cut.FindAll("[data-testid='table-tva-create-btn']").Should().BeEmpty();
        cut.FindAll("[data-testid='quick-action-edit']").Should().BeEmpty();
        cut.FindAll("[data-testid='quick-action-delete']").Should().BeEmpty();

        // La couverture reste informative, mais sans bouton « Créer la règle ».
        cut.FindAll("[data-testid='table-tva-coverage-entry']").Should().ContainSingle();
        cut.FindAll("[data-testid='table-tva-coverage-create-99']").Should().BeEmpty();
    }

    [Fact]
    public void Edit_controls_are_shown_with_settings_permission()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), coverage: CoverageWithAbsent()))
            .Add(v => v.CanEdit, true));

        cut.FindAll("[data-testid='table-tva-create-btn']").Should().ContainSingle();
        cut.FindAll("[data-testid='quick-action-edit']").Should().NotBeEmpty();
        cut.FindAll("[data-testid='quick-action-delete']").Should().NotBeEmpty();
        cut.FindAll("[data-testid='table-tva-coverage-create-99']").Should().ContainSingle();
    }

    [Fact]
    public void Coverage_create_button_invokes_callback_with_the_absent_regime()
    {
        RegimeCoverageDto? captured = null;
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), coverage: CoverageWithAbsent()))
            .Add(v => v.CanEdit, true)
            .Add(v => v.OnCreateRuleForRegime, r => { captured = r; }));

        cut.Find("[data-testid='table-tva-coverage-create-99']").Click();

        captured.Should().NotBeNull();
        captured!.Code.Should().Be("99");
    }

    [Fact]
    public void Create_button_invokes_callback()
    {
        var opened = false;
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable()))
            .Add(v => v.CanEdit, true)
            .Add(v => v.OnCreateRule, () => { opened = true; }));

        cut.Find("[data-testid='table-tva-create-btn']").Click();

        opened.Should().BeTrue();
    }

    [Fact]
    public void Editor_is_rendered_when_open()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable()))
            .Add(v => v.CanEdit, true)
            .Add(v => v.EditorOpen, true)
            .Add(v => v.EditorIsCreate, true)
            .Add(v => v.EditorModel, new TvaRuleFormModel()));

        cut.FindAll("[data-testid='tva-rule-editor']").Should().ContainSingle();
    }

    [Fact]
    public void Delete_confirmation_renders_and_invokes_callbacks()
    {
        var confirmed = false;
        var cancelled = false;
        var rule = NotValidatedTable().Rules[0];

        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable()))
            .Add(v => v.CanEdit, true)
            .Add(v => v.DeleteTarget, rule)
            .Add(v => v.OnConfirmDelete, () => { confirmed = true; })
            .Add(v => v.OnCancelDelete, () => { cancelled = true; }));

        cut.Find("[data-testid='table-tva-delete-confirm']").TextContent.Should().Contain(rule.SourceRegimeCode);

        cut.Find("[data-testid='table-tva-delete-confirm-btn']").Click();
        confirmed.Should().BeTrue();

        cut.Find("[data-testid='table-tva-delete-cancel-btn']").Click();
        cancelled.Should().BeTrue();
    }

    [Fact]
    public void No_table_with_permission_shows_create_table_button()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(null))
            .Add(v => v.CanEdit, true));

        cut.FindAll("[data-testid='table-tva-none']").Should().ContainSingle();
        cut.FindAll("[data-testid='table-tva-create-table-btn']").Should().ContainSingle();
    }

    [Fact]
    public void No_table_without_permission_hides_create_table_button()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(null))
            .Add(v => v.CanEdit, false));

        cut.FindAll("[data-testid='table-tva-none']").Should().ContainSingle();
        cut.FindAll("[data-testid='table-tva-create-table-btn']").Should().BeEmpty();
    }

    [Fact]
    public void Create_table_button_invokes_callback()
    {
        var created = false;
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(null))
            .Add(v => v.CanEdit, true)
            .Add(v => v.OnCreateTable, () => { created = true; }));

        cut.Find("[data-testid='table-tva-create-table-btn']").Click();

        created.Should().BeTrue();
    }

    [Fact]
    public void Create_table_error_is_displayed_on_empty_state()
    {
        const string ErrorMessage = "La création de la table a échoué. Réessayez plus tard.";

        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(null))
            .Add(v => v.CanEdit, true)
            .Add(v => v.CreateError, ErrorMessage));

        cut.FindAll("[data-testid='table-tva-create-error']").Should().ContainSingle();
        cut.Find("[data-testid='table-tva-create-error']").TextContent.Should().Contain(ErrorMessage);
    }

    [Fact]
    public void CreateTable_changelog_entry_is_rendered_in_french()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), WithCreateTableChangeLog()))
            .Add(v => v.CanValidate, false));

        cut.FindAll("[data-testid='table-tva-changelog-entry']").Should().ContainSingle();
        cut.Markup.Should().Contain("Table créée");
    }

    private static TvaMappingTableViewModel ModelWith(
        MappingTableDto? table,
        IReadOnlyList<MappingChangeLogEntryDto>? changeLog = null,
        MappingCoverageReportDto? coverage = null) => new()
    {
        Table = table,
        ChangeLog = changeLog ?? Array.Empty<MappingChangeLogEntryDto>(),
        CurrentOperatorName = "Alice Martin",
        Coverage = coverage,
        EditOptions = EditOptions(),
    };

    private static TvaMappingEditOptionsDto EditOptions() => new()
    {
        Categories = [new TvaMappingOptionDto("S", "Taux normal"), new TvaMappingOptionDto("E", "Exonéré")],
        Parts = [new TvaMappingOptionDto("Adjudication", "Adjudication"), new TvaMappingOptionDto("Frais", "Frais")],
        RateModes = [new TvaMappingOptionDto("Fixed", "Taux fixe"), new TvaMappingOptionDto("ComputedFromSource", "Calculé depuis la source")],
        VatexCodes = [new TvaMappingOptionDto("VATEX-EU-J", "VATEX-EU-J — Collection")],
    };

    private static MappingCoverageReportDto CoverageWithAbsent() => new()
    {
        IsTableConfigured = true,
        MappingVersion = "v1",
        IsTableValidated = false,
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
    };

    private static MappingTableDto ValidatedTable() => BuildTable(
        isValidated: true,
        validatedBy: "Alice Martin",
        validatedDate: new DateOnly(2026, 6, 1));

    private static MappingTableDto NotValidatedTable() => BuildTable(
        isValidated: false,
        validatedBy: null,
        validatedDate: null);

    private static MappingTableDto BuildTable(bool isValidated, string? validatedBy, DateOnly? validatedDate) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        MappingVersion = "v1",
        ValidatedBy = validatedBy,
        ValidatedDate = validatedDate,
        IsValidated = isValidated,
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
            new MappingRuleDto
            {
                SourceRegimeCode = "EXO",
                Label = null,
                Part = "Autre",
                Category = "E",
                Vatex = "VATEX-EU-O",
                RateMode = "ComputedFromSource",
                RateValue = null,
            },
        ],
    };

    private static IReadOnlyList<MappingChangeLogEntryDto> WithChangeLog() =>
    [
        new MappingChangeLogEntryDto
        {
            Id = Guid.NewGuid(),
            ChangeType = "Validate",
            SourceRegimeCode = null,
            Part = null,
            MappingVersion = "v1",
            BeforeJson = null,
            AfterJson = null,
            OperatorId = Guid.NewGuid(),
            OperatorName = "Alice Martin",
            OccurredAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        },
        new MappingChangeLogEntryDto
        {
            Id = Guid.NewGuid(),
            ChangeType = "AddRule",
            SourceRegimeCode = "20",
            Part = "Adjudication",
            MappingVersion = "v1",
            BeforeJson = null,
            AfterJson = "{}",
            OperatorId = Guid.NewGuid(),
            OperatorName = "Alice Martin",
            OccurredAt = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
        },
    ];

    private static IReadOnlyList<MappingChangeLogEntryDto> WithCreateTableChangeLog() =>
    [
        new MappingChangeLogEntryDto
        {
            Id = Guid.NewGuid(),
            ChangeType = "CreateTable",
            SourceRegimeCode = null,
            Part = null,
            MappingVersion = "1",
            BeforeJson = null,
            AfterJson = "{}",
            OperatorId = Guid.NewGuid(),
            OperatorName = "Alice Martin",
            OccurredAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
        },
    ];

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

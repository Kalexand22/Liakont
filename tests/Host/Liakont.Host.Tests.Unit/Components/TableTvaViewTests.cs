namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly string[] RegimesAfterAdd = ["20", "44"];

    public TableTvaViewTests()
    {
        // Radzen / grille s'appuient sur le JS interop — loose mode capte tous les appels.
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddLogging();
        Services.AddLocalization();

        // Graphe Common.UI réel (DeclaredListPage → StratumDataGrid) ; acteur anonyme (UserId == Empty)
        // pour court-circuiter les lectures de préférences par utilisateur — aucune base requise.
        Services.AddCommonUI();
        Services.AddBrowserTimeZoneStub();
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
    public void Rules_grid_refreshes_when_the_model_changes_without_navigation()
    {
        // Régression FIX04a : le gabarit DeclaredListPage charge ses lignes une seule fois
        // (OnInitializedAsync) et ne ré-interroge pas LoadItems quand le modèle parent est rechargé.
        // La clé de recréation (@key="RulesGridKey") doit rendre une règle ajoutée visible SANS navigation.
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(TableWithRules("20")))
            .Add(v => v.CanEdit, true));

        // Au départ : la grille n'affiche que le régime 20 (on cible les cellules, pas le markup entier —
        // les id générés par la grille pourraient contenir n'importe quelle sous-chaîne).
        RegimeCells(cut).Should().ContainSingle().Which.Should().Be("20");

        // Rechargement du modèle après mutation (nouvelle règle « 44 ») — exactement ce que fait la page.
        cut.Render(p => p
            .Add(v => v.Model, ModelWith(TableWithRules("20", "44"))));

        // La grille reflète la mutation immédiatement, sans navigation.
        cut.WaitForAssertion(() => RegimeCells(cut).Should().BeEquivalentTo(RegimesAfterAdd));
    }

    [Fact]
    public void Rules_grid_refreshes_when_only_label_changes_in_place()
    {
        // Régression FIX04a (édition en place du libellé) : MappingVersion et Rules.Count restent
        // identiques après UpdateRule → seul Label change. La clé doit détecter ce changement et
        // forcer la recréation de la grille. Ce test échoue avec l'ancienne clé (sans rule.Label)
        // et passe avec la nouvelle.
        var fixedUpdatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(TableWithLabeledRule("Ancien libellé", fixedUpdatedAt)))
            .Add(v => v.CanEdit, true));

        // Re-rendu avec MÊME MappingVersion, MÊME UpdatedAt, MÊME SourceRegimeCode/Part/Category/RateMode —
        // SEUL Label change : c'est ce que fait la page après un UpdateRule.
        cut.Render(p => p
            .Add(v => v.Model, ModelWith(TableWithLabeledRule("Nouveau libellé", fixedUpdatedAt)))
            .Add(v => v.CanEdit, true));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='grid-cell-Label']").TextContent.Trim().Should().Be("Nouveau libellé"));
    }

    private static List<string> RegimeCells(IRenderedComponent<TableTvaView> cut) =>
        cut.FindAll("[data-testid='grid-cell-SourceRegimeCode']")
            .Select(cell => cell.TextContent.Trim())
            .ToList();

    private static MappingTableDto TableWithLabeledRule(string label, DateTimeOffset updatedAt) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        MappingVersion = "v1",
        ValidatedBy = null,
        ValidatedDate = null,
        IsValidated = false,
        DefaultBehavior = "Block",
        CreatedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = updatedAt,
        Rules =
        [
            new MappingRuleDto
            {
                SourceRegimeCode = "20",
                Label = label,
                Part = "Autre",
                Category = "E",
                Vatex = null,
                RateMode = "ComputedFromSource",
                RateValue = null,
            },
        ],
    };

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

    [Fact]
    public void Auction_vertical_section_is_hidden_when_tenant_not_resolved()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(null, tenantResolved: false))
            .Add(v => v.CanEdit, true));

        cut.FindAll("[data-testid='table-tva-auction-vertical']").Should().BeEmpty();
    }

    [Fact]
    public void Auction_vertical_toggle_is_shown_for_editor_when_tenant_resolved()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), tenantResolved: true, auctionVerticalEnabled: false))
            .Add(v => v.CanEdit, true));

        cut.FindAll("[data-testid='table-tva-auction-vertical-state']").Should().ContainSingle();
        cut.Find("[data-testid='table-tva-auction-vertical-toggle']").TextContent.Should().Contain("Activer");
    }

    [Fact]
    public void Auction_vertical_toggle_is_hidden_without_settings_permission()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), tenantResolved: true))
            .Add(v => v.CanEdit, false));

        // L'état reste informatif, mais aucun bouton de bascule sans liakont.settings.
        cut.FindAll("[data-testid='table-tva-auction-vertical-state']").Should().ContainSingle();
        cut.FindAll("[data-testid='table-tva-auction-vertical-toggle']").Should().BeEmpty();
    }

    [Fact]
    public void Toggling_auction_vertical_invokes_the_callback()
    {
        var toggled = false;
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), tenantResolved: true))
            .Add(v => v.CanEdit, true)
            .Add(v => v.OnToggleAuctionVertical, () => { toggled = true; }));

        cut.Find("[data-testid='table-tva-auction-vertical-toggle']").Click();

        toggled.Should().BeTrue();
    }

    [Fact]
    public void Dead_rules_consistency_section_is_rendered()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), tenantResolved: true, consistency: ConsistencyWithDeadRule()))
            .Add(v => v.CanEdit, true));

        cut.FindAll("[data-testid='table-tva-consistency']").Should().ContainSingle();
        cut.FindAll("[data-testid='table-tva-consistency-entry']").Should().ContainSingle();
        cut.Find("[data-testid='table-tva-consistency-entry']").TextContent.Should().Contain("ADJ");
        cut.Find("[data-testid='table-tva-consistency-entry']").TextContent.Should().Contain("part non consultée par le pipeline");
    }

    [Fact]
    public void Dead_rules_warning_appears_in_the_validate_confirm_dialog()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), tenantResolved: true, consistency: ConsistencyWithDeadRule()))
            .Add(v => v.CanValidate, true)
            .Add(v => v.ConfirmOpen, true));

        // L'avertissement signale les règles inopérantes sans bloquer la validation (CLAUDE.md n°3).
        cut.FindAll("[data-testid='table-tva-validate-deadrules-warning']").Should().ContainSingle();
        cut.Find("[data-testid='table-tva-confirm-btn']").Should().NotBeNull();
    }

    [Fact]
    public void No_consistency_section_when_no_dead_rules()
    {
        var emptyConsistency = new MappingConsistencyReportDto
        {
            IsTableConfigured = true,
            DeadRules = Array.Empty<DeadMappingRuleDto>(),
        };
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), tenantResolved: true, consistency: emptyConsistency))
            .Add(v => v.CanEdit, true));

        cut.FindAll("[data-testid='table-tva-consistency']").Should().BeEmpty();
    }

    // ── Composante (décision E2 / lot FIX2) ─────────────────────────────
    [Fact]
    public void Composante_column_is_shown_with_hors_encheres_value_when_vertical_on()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(SingleRuleTable("Autre"), tenantResolved: true, auctionVerticalEnabled: true))
            .Add(v => v.CanEdit, false));

        // Colonne « Composante » (clé technique « Part ») présente ; valeur Autre rendue « Hors Enchères ».
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='grid-cell-Part']").TextContent.Trim().Should().Be("Hors Enchères"));
    }

    [Fact]
    public void Composante_column_is_hidden_when_vertical_off()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(SingleRuleTable("Autre"), tenantResolved: true, auctionVerticalEnabled: false))
            .Add(v => v.CanEdit, false));

        // Vertical OFF : la notion n'apparaît nulle part — aucune colonne « Composante » dans la grille.
        cut.WaitForAssertion(() => RegimeCells(cut).Should().ContainSingle());
        cut.FindAll("[data-testid='grid-cell-Part']").Should().BeEmpty();
    }

    [Fact]
    public void Dead_rule_uses_composante_vocabulary_when_vertical_on()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), tenantResolved: true, auctionVerticalEnabled: true, consistency: ConsistencyWithDeadRule()))
            .Add(v => v.CanEdit, true));

        var entry = cut.Find("[data-testid='table-tva-consistency-entry']").TextContent;
        entry.Should().Contain("ADJ");
        entry.Should().Contain("(Adjudication)");
        entry.Should().Contain("Composante non consultée par le pipeline");
    }

    [Fact]
    public void Changelog_renders_before_after_diff_for_an_update()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), UpdateChangeLog()))
            .Add(v => v.CanValidate, false));

        cut.FindAll("[data-testid='table-tva-changelog-diff']").Should().ContainSingle();
        cut.FindAll("[data-testid='table-tva-changelog-diff-line']").Should().NotBeEmpty();

        var markup = cut.Markup;
        markup.Should().Contain("Ancien libellé → Nouveau libellé");
        markup.Should().Contain("S → AA");
        markup.Should().Contain("20 % → 10 %");
    }

    [Fact]
    public void Changelog_renders_created_values_for_an_add()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), AddChangeLog()))
            .Add(v => v.CanValidate, false));

        cut.FindAll("[data-testid='table-tva-changelog-diff-line']").Should().NotBeEmpty();
        var markup = cut.Markup;
        markup.Should().Contain("Régime source");
        markup.Should().Contain("44");
        markup.Should().Contain("Calculé depuis la source");
    }

    [Fact]
    public void Changelog_renders_removed_values_for_a_remove()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), RemoveChangeLog()))
            .Add(v => v.CanValidate, false));

        cut.FindAll("[data-testid='table-tva-changelog-diff-line']").Should().NotBeEmpty();
        var markup = cut.Markup;
        markup.Should().Contain("Obsolète");
        markup.Should().Contain("VATEX-EU-O");
    }

    [Fact]
    public void Changelog_renders_validator_diff_for_a_validate()
    {
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), ValidateChangeLog()))
            .Add(v => v.CanValidate, false));

        cut.FindAll("[data-testid='table-tva-changelog-diff-line']").Should().NotBeEmpty();
        var markup = cut.Markup;
        markup.Should().Contain("Validé par");
        markup.Should().Contain("Alice Martin");
        markup.Should().Contain("2026-06-01");
    }

    [Fact]
    public void Changelog_update_renders_a_cleared_field_as_emptied_not_as_a_current_value()
    {
        // Bug de rendu (revue FIX204) : passer une règle de taux fixe au mode calculé SUPPRIME le taux
        // fixe (RateValue absent de AfterJson). Le diff doit montrer « 20 % → (vide) », jamais « 20 % »
        // seul (qui se confondrait avec une valeur inchangée) — journal de conformité.
        var cut = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), ClearedFieldUpdateChangeLog()))
            .Add(v => v.CanValidate, false));

        var markup = cut.Markup;
        markup.Should().Contain("20 % → (vide)");
        markup.Should().Contain("Taux fixe → Calculé depuis la source");
    }

    [Fact]
    public void Changelog_diff_shows_composante_only_when_vertical_on()
    {
        // Vertical ON : la composante modifiée apparaît, valeur Autre → « Hors Enchères ».
        var on = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), PartOnlyUpdateChangeLog(), tenantResolved: true, auctionVerticalEnabled: true))
            .Add(v => v.CanValidate, false));
        on.Markup.Should().Contain("Composante");
        on.Markup.Should().Contain("Adjudication → Hors Enchères");

        // Vertical OFF : la composante n'est jamais mentionnée — la seule modif portant sur la part
        // ne produit donc AUCUNE ligne de diff (E2).
        var off = Render<TableTvaView>(p => p
            .Add(v => v.Model, ModelWith(NotValidatedTable(), PartOnlyUpdateChangeLog()))
            .Add(v => v.CanValidate, false));
        off.Markup.Should().NotContain("Composante");
        off.FindAll("[data-testid='table-tva-changelog-diff-line']").Should().BeEmpty();
    }

    private static MappingTableDto SingleRuleTable(string part) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        MappingVersion = "v1",
        ValidatedBy = null,
        ValidatedDate = null,
        IsValidated = false,
        DefaultBehavior = "Block",
        CreatedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = null,
        Rules =
        [
            new MappingRuleDto
            {
                SourceRegimeCode = "20",
                Label = "TVA 20 %",
                Part = part,
                Category = "S",
                Vatex = null,
                RateMode = "Fixed",
                RateValue = 20m,
            },
        ],
    };

    private static IReadOnlyList<MappingChangeLogEntryDto> UpdateChangeLog() =>
    [
        new MappingChangeLogEntryDto
        {
            Id = Guid.NewGuid(),
            ChangeType = "UpdateRule",
            SourceRegimeCode = "20",
            Part = "Autre",
            MappingVersion = "v1",
            BeforeJson = """{"SourceRegimeCode":"20","Label":"Ancien libellé","Part":"Autre","Category":"S","RateMode":"Fixed","RateValue":20}""",
            AfterJson = """{"SourceRegimeCode":"20","Label":"Nouveau libellé","Part":"Autre","Category":"AA","RateMode":"Fixed","RateValue":10}""",
            OperatorId = Guid.NewGuid(),
            OperatorName = "Alice Martin",
            OccurredAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    private static IReadOnlyList<MappingChangeLogEntryDto> AddChangeLog() =>
    [
        new MappingChangeLogEntryDto
        {
            Id = Guid.NewGuid(),
            ChangeType = "AddRule",
            SourceRegimeCode = "44",
            Part = "Autre",
            MappingVersion = "v1",
            BeforeJson = null,
            AfterJson = """{"SourceRegimeCode":"44","Label":"Intracommunautaire","Part":"Autre","Category":"K","RateMode":"ComputedFromSource"}""",
            OperatorId = Guid.NewGuid(),
            OperatorName = "Alice Martin",
            OccurredAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    private static IReadOnlyList<MappingChangeLogEntryDto> RemoveChangeLog() =>
    [
        new MappingChangeLogEntryDto
        {
            Id = Guid.NewGuid(),
            ChangeType = "RemoveRule",
            SourceRegimeCode = "99",
            Part = "Autre",
            MappingVersion = "v1",
            BeforeJson = """{"SourceRegimeCode":"99","Label":"Obsolète","Part":"Autre","Category":"E","Vatex":"VATEX-EU-O","RateMode":"ComputedFromSource"}""",
            AfterJson = null,
            OperatorId = Guid.NewGuid(),
            OperatorName = "Alice Martin",
            OccurredAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    private static IReadOnlyList<MappingChangeLogEntryDto> ValidateChangeLog() =>
    [
        new MappingChangeLogEntryDto
        {
            Id = Guid.NewGuid(),
            ChangeType = "Validate",
            SourceRegimeCode = null,
            Part = null,
            MappingVersion = "v1",
            BeforeJson = "{}",
            AfterJson = """{"ValidatedBy":"Alice Martin","ValidatedDate":"2026-06-01"}""",
            OperatorId = Guid.NewGuid(),
            OperatorName = "Alice Martin",
            OccurredAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    private static IReadOnlyList<MappingChangeLogEntryDto> PartOnlyUpdateChangeLog() =>
    [
        new MappingChangeLogEntryDto
        {
            Id = Guid.NewGuid(),
            ChangeType = "UpdateRule",
            SourceRegimeCode = "20",
            Part = "Autre",
            MappingVersion = "v1",
            BeforeJson = """{"SourceRegimeCode":"20","Part":"Adjudication","Category":"S","RateMode":"Fixed","RateValue":20}""",
            AfterJson = """{"SourceRegimeCode":"20","Part":"Autre","Category":"S","RateMode":"Fixed","RateValue":20}""",
            OperatorId = Guid.NewGuid(),
            OperatorName = "Alice Martin",
            OccurredAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    private static IReadOnlyList<MappingChangeLogEntryDto> ClearedFieldUpdateChangeLog() =>
    [
        new MappingChangeLogEntryDto
        {
            Id = Guid.NewGuid(),
            ChangeType = "UpdateRule",
            SourceRegimeCode = "20",
            Part = "Autre",
            MappingVersion = "v1",
            BeforeJson = """{"SourceRegimeCode":"20","Category":"S","RateMode":"Fixed","RateValue":20}""",
            AfterJson = """{"SourceRegimeCode":"20","Category":"S","RateMode":"ComputedFromSource"}""",
            OperatorId = Guid.NewGuid(),
            OperatorName = "Alice Martin",
            OccurredAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        },
    ];

    private static TvaMappingTableViewModel ModelWith(
        MappingTableDto? table,
        IReadOnlyList<MappingChangeLogEntryDto>? changeLog = null,
        MappingCoverageReportDto? coverage = null,
        bool tenantResolved = false,
        bool auctionVerticalEnabled = false,
        MappingConsistencyReportDto? consistency = null) => new()
    {
        Table = table,
        ChangeLog = changeLog ?? Array.Empty<MappingChangeLogEntryDto>(),
        CurrentOperatorName = "Alice Martin",
        Coverage = coverage,
        TenantResolved = tenantResolved,
        AuctionVerticalEnabled = auctionVerticalEnabled,
        Consistency = consistency,
        EditOptions = EditOptions(),
    };

    private static MappingConsistencyReportDto ConsistencyWithDeadRule() => new()
    {
        IsTableConfigured = true,
        DeadRules =
        [
            new DeadMappingRuleDto
            {
                SourceRegimeCode = "ADJ",
                Part = "Adjudication",
                Label = "Adjudication héritée",
                Reasons = ["PartNotConsulted"],
            },
        ],
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

    private static MappingTableDto TableWithRules(params string[] sourceRegimeCodes) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        MappingVersion = "v1",
        ValidatedBy = null,
        ValidatedDate = null,
        IsValidated = false,
        DefaultBehavior = "Block",
        CreatedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = null,
        Rules = sourceRegimeCodes
            .Select(code => new MappingRuleDto
            {
                SourceRegimeCode = code,
                Label = null,
                Part = "Autre",
                Category = "E",
                Vatex = "VATEX-EU-O",
                RateMode = "ComputedFromSource",
                RateValue = null,
            })
            .ToList(),
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

namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class StratumFilterBuilderTests : BunitContext
{
    private readonly TestColumnRegistry _registry = new();

    public StratumFilterBuilderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ISavedFilterService>(new FakeSavedFilterService());
    }

    [Fact]
    public void ShouldNotRenderDialogWhenNotVisible()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, false)
            .Add(f => f.Registry, _registry));

        cut.FindAll(".stratum-filter-builder").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldRenderDialogWhenVisible()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        cut.Find(".stratum-filter-builder").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderSavedFilterDropdown()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        cut.Find(".stratum-filter-builder__saved-select").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderLogicToggleButtons()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        var logicButtons = cut.FindAll(".stratum-filter-builder__logic-btn");
        logicButtons.Count.Should().BeGreaterThanOrEqualTo(2, "should have ET and OU buttons");
    }

    [Fact]
    public void ShouldRenderActionButtons()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        var markup = cut.Markup;
        markup.Should().Contain("Effacer tout");
        markup.Should().Contain("Annuler");
        markup.Should().Contain("Appliquer");
    }

    [Fact]
    public void ShouldAddCriterionWhenAddButtonClicked()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        var addBtn = cut.Find("[aria-label='Ajouter un critère']");
        addBtn.Click();

        cut.FindAll(".stratum-filter-builder__row").Count.Should().Be(1);
    }

    [Fact]
    public void ShouldRemoveCriterionWhenRemoveButtonClicked()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        var addBtn = cut.Find("[aria-label='Ajouter un critère']");
        addBtn.Click();
        addBtn.Click();
        cut.FindAll(".stratum-filter-builder__row").Count.Should().Be(2);

        var removeBtn = cut.Find("[aria-label='Supprimer ce critère']");
        removeBtn.Click();
        cut.FindAll(".stratum-filter-builder__row").Count.Should().Be(1);
    }

    [Fact]
    public void ShouldAddSubGroupWhenGroupButtonClicked()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        var addGroupBtn = cut.Find("[aria-label='Ajouter un sous-groupe']");
        addGroupBtn.Click();

        cut.FindAll(".stratum-filter-builder__group--nested").Count.Should().Be(1);
    }

    [Fact]
    public void ShouldRemoveSubGroupWhenRemoveGroupButtonClicked()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        cut.Find("[aria-label='Ajouter un sous-groupe']").Click();
        cut.FindAll(".stratum-filter-builder__group--nested").Count.Should().Be(1);

        cut.Find("[aria-label='Supprimer ce sous-groupe']").Click();
        cut.FindAll(".stratum-filter-builder__group--nested").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldShowFieldSelectionOptionsGroupedByCategory()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        cut.Find("[aria-label='Ajouter un critère']").Click();

        var fieldSelect = cut.Find(".stratum-filter-builder__field-select");
        fieldSelect.Should().NotBeNull();

        var optgroups = cut.FindAll(".stratum-filter-builder__field-select optgroup");
        optgroups.Count.Should().BeGreaterThanOrEqualTo(1, "fields should be grouped by category");
    }

    [Fact]
    public void ShouldUpdateOperatorsWhenFieldChanges()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        cut.Find("[aria-label='Ajouter un critère']").Click();
        var fieldSelect = cut.Find(".stratum-filter-builder__field-select");
        fieldSelect.Change("Name");

        var operatorSelect = cut.Find(".stratum-filter-builder__operator-select");
        operatorSelect.OuterHtml.Should().Contain("Contient");
    }

    [Fact]
    public void BooleanFieldShouldNotShowContainsOperator()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        cut.Find("[aria-label='Ajouter un critère']").Click();
        var fieldSelect = cut.Find(".stratum-filter-builder__field-select");
        fieldSelect.Change("IsActive");

        var operatorSelect = cut.Find(".stratum-filter-builder__operator-select");
        operatorSelect.OuterHtml.Should().NotContain("Contient");
    }

    [Fact]
    public async Task ShouldFireOnApplyWithNullWhenNoCriteria()
    {
        FilterGroup? appliedFilter = null;
        var applied = false;

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.OnApply, EventCallback.Factory.Create<FilterGroup?>(this, f =>
            {
                appliedFilter = f;
                applied = true;
            })));

        cut.Find("button.btn-primary").Click();

        applied.Should().BeTrue();
        appliedFilter.Should().BeNull();
    }

    [Fact]
    public void ShouldClearAllCriteriaWhenClearButtonClicked()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry));

        cut.Find("[aria-label='Ajouter un critère']").Click();
        cut.FindAll(".stratum-filter-builder__row").Count.Should().Be(1);

        cut.Find("[aria-label='Effacer tous les critères']").Click();
        cut.FindAll(".stratum-filter-builder__row").Count.Should().Be(0);
    }

    [Fact]
    public async Task ShouldFireOnCancelWhenCancelButtonClicked()
    {
        var cancelled = false;

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true)));

        var cancelBtn = cut.FindAll("button")
            .First(b => b.TextContent.Trim() == "Annuler");
        cancelBtn.Click();

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void ShouldPreFillCriteriaFromActiveFilter()
    {
        var activeFilter = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "test"),
                new("Amount", FilterOperator.GreaterThan, 100m),
            });

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.ActiveFilter, activeFilter));

        cut.FindAll(".stratum-filter-builder__row").Count.Should().Be(2);
    }

    [Fact]
    public void ShouldShowTwoValueInputsForBetweenOperator()
    {
        var activeFilter = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion>
            {
                new("Amount", FilterOperator.Between, 100m, 500m),
            });

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.ActiveFilter, activeFilter));

        cut.FindAll(".stratum-filter-builder__between-sep").Count.Should().Be(1);
    }

    [Fact]
    public void ShouldHideValueInputForNullOperators()
    {
        var activeFilter = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion>
            {
                new("DueDate", FilterOperator.IsNull, null),
            });

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.ActiveFilter, activeFilter));

        cut.FindAll(".stratum-filter-builder__value-input").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldLoadSavedFiltersWhenVisibleWithService()
    {
        var service = new FakeSavedFilterService();

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, Guid.NewGuid())
            .Add(f => f.SavedFilterService, service));

        service.ListCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void ShouldRenderSavedFiltersInDropdown()
    {
        var userId = Guid.NewGuid();
        var filterGroup = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion> { new("Name", FilterOperator.Equals, "test") });
        var service = new FakeSavedFilterService(
            new SavedFilter(
                Guid.NewGuid(),
                userId,
                "Test.Grid.Main",
                "Mon filtre",
                filterGroup,
                false,
                SharedScope.None,
                DateTimeOffset.UtcNow,
                null));

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        cut.Find(".stratum-filter-builder__saved-select").OuterHtml.Should().Contain("Mon filtre");
    }

    [Fact]
    public void ShouldShowSaveDialogWhenSaveClicked()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.SavedFilterService, new FakeSavedFilterService()));

        cut.Find("[aria-label='Ajouter un critère']").Click();
        var fieldSelect = cut.Find(".stratum-filter-builder__field-select");
        fieldSelect.Change("Name");

        var saveBtn = cut.Find("[data-testid='stratum-filter-builder__save']");
        saveBtn.Click();

        cut.Find("[data-testid='stratum-filter-builder__save-dialog']").Should().NotBeNull();
    }

    [Fact]
    public void SaveButtonShouldBeDisabledWhenNoCriteria()
    {
        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.SavedFilterService, new FakeSavedFilterService()));

        var saveBtn = cut.Find("[data-testid='stratum-filter-builder__save']");
        saveBtn.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void LoadingSavedFilterShouldSwitchToUpdateAndSaveAsButtons()
    {
        var userId = Guid.NewGuid();
        var loaded = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Mon filtre",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "acme"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(loaded);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        // Create + Update-mode buttons must not coexist before load.
        cut.FindAll("[data-testid='stratum-filter-builder__save']").Count.Should().Be(1);
        cut.FindAll("[data-testid='stratum-filter-builder__update']").Count.Should().Be(0);

        // Load the saved filter from the dropdown.
        cut.Find(".stratum-filter-builder__saved-select").Change(loaded.Id.ToString());

        // After load: save-as + update present, plain save gone.
        cut.FindAll("[data-testid='stratum-filter-builder__save']").Count.Should().Be(0);
        cut.Find("[data-testid='stratum-filter-builder__update']").Should().NotBeNull();
        cut.Find("[data-testid='stratum-filter-builder__save-as']").Should().NotBeNull();
    }

    [Fact]
    public void LoadedCleanFilterShouldDisableUpdateButton()
    {
        var userId = Guid.NewGuid();
        var loaded = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Mon filtre",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "acme"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(loaded);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        cut.Find(".stratum-filter-builder__saved-select").Change(loaded.Id.ToString());

        var updateBtn = cut.Find("[data-testid='stratum-filter-builder__update']");
        updateBtn.HasAttribute("disabled").Should().BeTrue("a freshly loaded filter is not dirty");
        cut.FindAll("[data-testid='stratum-filter-builder__dirty-chip']").Count.Should().Be(0);
    }

    [Fact]
    public void EditingLoadedFilterShouldEnableUpdateButtonAndShowDirtyChip()
    {
        var userId = Guid.NewGuid();
        var loaded = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Mon filtre",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "acme"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(loaded);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        cut.Find(".stratum-filter-builder__saved-select").Change(loaded.Id.ToString());

        // Mutate the loaded criterion value to mark the group dirty.
        cut.Find(".stratum-filter-builder__value-input").Change("widgets");

        cut.Find("[data-testid='stratum-filter-builder__dirty-chip']").Should().NotBeNull();
        var updateBtn = cut.Find("[data-testid='stratum-filter-builder__update']");
        updateBtn.HasAttribute("disabled").Should().BeFalse("dirty filter should enable the update button");
    }

    [Fact]
    public void UpdateFilterShouldPersistWithSameIdAndClearDirty()
    {
        var userId = Guid.NewGuid();
        var loadedId = Guid.NewGuid();
        var loaded = new SavedFilter(
            loadedId,
            userId,
            "Test.Grid.Main",
            "Mon filtre",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "acme"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(loaded);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        cut.Find(".stratum-filter-builder__saved-select").Change(loaded.Id.ToString());
        cut.Find(".stratum-filter-builder__value-input").Change("widgets");

        // Open confirm dialog, then confirm update.
        cut.Find("[data-testid='stratum-filter-builder__update']").Click();
        cut.Find("[data-testid='stratum-filter-builder__confirm-update']").Click();

        service.LastSaved.Should().NotBeNull();
        service.LastSaved!.Id.Should().Be(loadedId, "update must reuse the loaded filter id (upsert)");
        service.LastSaved.FilterGroup.Criteria[0].Value.Should().Be("widgets");
        service.LastSaved.UpdatedAt.Should().NotBeNull();

        // After a successful update, the dirty chip disappears.
        cut.FindAll("[data-testid='stratum-filter-builder__dirty-chip']").Count.Should().Be(0);
    }

    [Fact]
    public void SaveAsShouldCreateNewFilterWithFreshIdAndSwitchToLoaded()
    {
        var userId = Guid.NewGuid();
        var original = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Mon filtre",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "acme"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(original);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        cut.Find(".stratum-filter-builder__saved-select").Change(original.Id.ToString());
        cut.Find(".stratum-filter-builder__value-input").Change("widgets");

        // Save-as opens the name dialog pre-filled with "(copie)" hint.
        cut.Find("[data-testid='stratum-filter-builder__save-as']").Click();
        var saveDialog = cut.Find("[data-testid='stratum-filter-builder__save-dialog']");
        saveDialog.Should().NotBeNull();

        var nameInput = cut.Find(".stratum-filter-builder__save-input");
        nameInput.Input("Filtre widgets");
        cut.FindAll("button.btn-primary")
            .First(b => b.TextContent.Trim() == "Enregistrer")
            .Click();

        service.LastSaved.Should().NotBeNull();
        service.LastSaved!.Id.Should().NotBe(original.Id, "save-as must mint a fresh id");
        service.LastSaved.Name.Should().Be("Filtre widgets");
    }

    [Fact]
    public void CancellingUpdateConfirmShouldNotPersistAndShouldKeepDirtyChip()
    {
        var userId = Guid.NewGuid();
        var loaded = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Mon filtre",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "acme"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(loaded);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        cut.Find(".stratum-filter-builder__saved-select").Change(loaded.Id.ToString());
        cut.Find(".stratum-filter-builder__value-input").Change("widgets");

        // Open the update confirm dialog.
        cut.Find("[data-testid='stratum-filter-builder__update']").Click();
        cut.Find("[data-testid='stratum-filter-builder__update-confirm']").Should().NotBeNull();

        // Cancel via the first "Annuler" button inside the confirm dialog.
        var confirmDialog = cut.Find("[data-testid='stratum-filter-builder__update-confirm']");
        var cancelBtn = confirmDialog.QuerySelectorAll("button")
            .First(b => b.TextContent.Trim() == "Annuler");
        ((AngleSharp.Dom.IElement)cancelBtn).Click();

        service.SaveCallCount.Should().Be(0, "cancelling must not call SaveAsync");
        cut.FindAll("[data-testid='stratum-filter-builder__update-confirm']").Count.Should().Be(0);
        cut.FindAll("[data-testid='stratum-filter-builder__dirty-chip']")
            .Count.Should().Be(1, "edits must remain pending after cancel — the dirty chip stays visible");
    }

    [Fact]
    public void SwitchingSavedFilterWhileDirtyShouldShowDiscardConfirmAndKeepLoaded()
    {
        var userId = Guid.NewGuid();
        var filterA = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Filtre A",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "alpha"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var filterB = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Filtre B",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "beta"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(filterA, filterB);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        cut.Find(".stratum-filter-builder__saved-select").Change(filterA.Id.ToString());

        // Mutate A so the builder is dirty.
        cut.Find(".stratum-filter-builder__value-input").Change("alpha-edited");
        cut.FindAll("[data-testid='stratum-filter-builder__dirty-chip']").Count.Should().Be(1);

        // Try to switch to B — expected: confirmation dialog, NO load of B yet.
        cut.Find(".stratum-filter-builder__saved-select").Change(filterB.Id.ToString());

        cut.FindAll("[data-testid='stratum-filter-builder__switch-confirm']")
            .Count.Should().Be(1, "picking a different saved filter while dirty must prompt for confirmation");

        // Working group must still reflect filter A's edited value, not B.
        var valueInput = cut.Find(".stratum-filter-builder__value-input");
        valueInput.GetAttribute("value").Should().Be("alpha-edited");
    }

    [Fact]
    public void CancellingDiscardSwitchShouldKeepCurrentLoadedFilter()
    {
        var userId = Guid.NewGuid();
        var filterA = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Filtre A",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "alpha"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var filterB = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Filtre B",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "beta"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(filterA, filterB);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        cut.Find(".stratum-filter-builder__saved-select").Change(filterA.Id.ToString());
        cut.Find(".stratum-filter-builder__value-input").Change("alpha-edited");
        cut.Find(".stratum-filter-builder__saved-select").Change(filterB.Id.ToString());

        // Cancel the switch: find the "Annuler" inside the switch-confirm dialog.
        var switchDialog = cut.Find("[data-testid='stratum-filter-builder__switch-confirm']");
        var cancel = switchDialog.QuerySelectorAll("button")
            .First(b => b.TextContent.Trim() == "Annuler");
        ((AngleSharp.Dom.IElement)cancel).Click();

        cut.FindAll("[data-testid='stratum-filter-builder__switch-confirm']").Count.Should().Be(0);
        cut.FindAll("[data-testid='stratum-filter-builder__dirty-chip']")
            .Count.Should().Be(1, "cancelling the switch must keep the dirty edits");

        var valueInput = cut.Find(".stratum-filter-builder__value-input");
        valueInput.GetAttribute("value").Should().Be("alpha-edited");
    }

    [Fact]
    public void ConfirmingDiscardSwitchShouldLoadPendingFilter()
    {
        var userId = Guid.NewGuid();
        var filterA = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Filtre A",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "alpha"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var filterB = new SavedFilter(
            Guid.NewGuid(),
            userId,
            "Test.Grid.Main",
            "Filtre B",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "beta"),
            }),
            false,
            SharedScope.None,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(filterA, filterB);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        cut.Find(".stratum-filter-builder__saved-select").Change(filterA.Id.ToString());
        cut.Find(".stratum-filter-builder__value-input").Change("alpha-edited");
        cut.Find(".stratum-filter-builder__saved-select").Change(filterB.Id.ToString());

        cut.Find("[data-testid='stratum-filter-builder__confirm-switch']").Click();

        cut.FindAll("[data-testid='stratum-filter-builder__switch-confirm']").Count.Should().Be(0);
        cut.FindAll("[data-testid='stratum-filter-builder__dirty-chip']")
            .Count.Should().Be(0, "after discarding A's edits, filter B is freshly loaded and clean");

        var valueInput = cut.Find(".stratum-filter-builder__value-input");
        valueInput.GetAttribute("value")
            .Should().Be("beta", "working group should now reflect filter B");
    }

    // ── GUX08 — shared filters & read-only semantics ─────────────
    [Fact]
    public void SavingWithShareCheckboxShouldPersistWithEveryoneScope()
    {
        var userId = Guid.NewGuid();
        var service = new FakeSavedFilterService();

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        // Author a minimal filter so the Save button is enabled.
        cut.Find("[aria-label='Ajouter un critère']").Click();
        cut.Find(".stratum-filter-builder__field-select").Change("Name");
        cut.Find(".stratum-filter-builder__value-input").Change("acme");

        cut.Find("[data-testid='stratum-filter-builder__save']").Click();

        // Tick the "Partager ce filtre" checkbox before submitting.
        var shareCheckbox = cut.Find("[data-testid='stratum-filter-builder__save-share']");
        shareCheckbox.Change(true);

        // Tenant scope radios must be visible once share is enabled.
        cut.FindAll("[data-testid='stratum-filter-builder__save-share-scope']")
            .Count.Should().Be(1, "enabling share should reveal the scope radios");

        // "Mon tenant" is the only selectable scope today: it is the checked
        // option and must NOT carry `disabled` (screen readers would otherwise
        // announce a contradictory "disabled, checked" state).
        var tenantRadio = cut.Find("[data-testid='stratum-filter-builder__share-scope-tenant']");
        tenantRadio.HasAttribute("checked").Should().BeTrue(
            "the tenant scope must be pre-selected when share is enabled");
        tenantRadio.HasAttribute("disabled").Should().BeFalse(
            "the tenant radio must be focusable — disabling a checked radio breaks a11y semantics");

        // "Groupe spécifique" remains disabled until Identity exposes a group API.
        var groupRadio = cut.Find("[data-testid='stratum-filter-builder__share-scope-group']");
        groupRadio.HasAttribute("disabled").Should().BeTrue(
            "group scope must stay disabled until Identity exposes a group API");
        groupRadio.HasAttribute("checked").Should().BeFalse();

        cut.Find(".stratum-filter-builder__save-input").Input("Filtre partagé");
        cut.FindAll("button.btn-primary")
            .First(b => b.TextContent.Trim() == "Enregistrer")
            .Click();

        service.LastSaved.Should().NotBeNull();
        service.LastSaved!.SharedWith.Should().Be(
            SharedScope.Everyone,
            "ticking the share checkbox must persist the filter with tenant-wide scope");
    }

    [Fact]
    public void SavingWithoutShareCheckboxShouldPersistWithNoneScope()
    {
        var userId = Guid.NewGuid();
        var service = new FakeSavedFilterService();

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, userId)
            .Add(f => f.SavedFilterService, service));

        cut.Find("[aria-label='Ajouter un critère']").Click();
        cut.Find(".stratum-filter-builder__field-select").Change("Name");
        cut.Find(".stratum-filter-builder__value-input").Change("acme");

        cut.Find("[data-testid='stratum-filter-builder__save']").Click();

        // Do NOT tick the share checkbox — scope radios should stay hidden.
        cut.FindAll("[data-testid='stratum-filter-builder__save-share-scope']")
            .Count.Should().Be(0, "scope radios must stay hidden until share is enabled");

        cut.Find(".stratum-filter-builder__save-input").Input("Filtre privé");
        cut.FindAll("button.btn-primary")
            .First(b => b.TextContent.Trim() == "Enregistrer")
            .Click();

        service.LastSaved.Should().NotBeNull();
        service.LastSaved!.SharedWith.Should().Be(SharedScope.None);
    }

    [Fact]
    public void LoadingFilterOwnedByAnotherUserShouldShowReadOnlyChipAndDisableUpdate()
    {
        var currentUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var shared = new SavedFilter(
            Guid.NewGuid(),
            otherUserId,
            "Test.Grid.Main",
            "Filtre équipe",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "acme"),
            }),
            false,
            SharedScope.Everyone,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(shared);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, currentUserId)
            .Add(f => f.SavedFilterService, service));

        // Dropdown must annotate foreign-authored filters as read-only.
        cut.Find(".stratum-filter-builder__saved-select").OuterHtml
            .Should()
            .Contain(
                "(lecture seule)",
                "foreign-owned filters must be labelled read-only in the dropdown");

        cut.Find(".stratum-filter-builder__saved-select").Change(shared.Id.ToString());

        cut.Find("[data-testid='stratum-filter-builder__readonly-chip']")
            .Should().NotBeNull("loading a foreign-owned filter should show the read-only chip");

        // Editing a field marks the builder dirty, but Update must remain disabled.
        cut.Find(".stratum-filter-builder__value-input").Change("widgets");
        var updateBtn = cut.Find("[data-testid='stratum-filter-builder__update']");
        updateBtn.HasAttribute("disabled").Should().BeTrue(
            "Update must stay disabled on a read-only filter, even when dirty");

        // Save-as stays available so the user can clone the filter.
        cut.Find("[data-testid='stratum-filter-builder__save-as']")
            .HasAttribute("disabled").Should().BeFalse(
                "Save-as must remain available so the user can clone the shared filter");
    }

    [Fact]
    public void SelectingReadOnlyFilterShouldDisableDeleteAndSetDefaultButtons()
    {
        var currentUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var shared = new SavedFilter(
            Guid.NewGuid(),
            otherUserId,
            "Test.Grid.Main",
            "Filtre équipe",
            new FilterGroup(FilterLogic.And, new List<FilterCriterion>
            {
                new("Name", FilterOperator.Contains, "acme"),
            }),
            false,
            SharedScope.Everyone,
            DateTimeOffset.UtcNow,
            null);
        var service = new FakeSavedFilterService(shared);

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.GridKey, "Test.Grid.Main")
            .Add(f => f.UserId, currentUserId)
            .Add(f => f.SavedFilterService, service));

        cut.Find(".stratum-filter-builder__saved-select").Change(shared.Id.ToString());

        var deleteBtn = cut.Find("[aria-label='Supprimer le filtre sélectionné']");
        deleteBtn.HasAttribute("disabled").Should().BeTrue(
            "Delete must be disabled when the selected filter is owned by someone else");

        var starBtn = cut.Find("[aria-label='Définir comme filtre par défaut']");
        starBtn.HasAttribute("disabled").Should().BeTrue(
            "Set-default must be disabled when the selected filter is owned by someone else");
    }

    [Fact]
    public void ShouldRenderSelectForBooleanValueInput()
    {
        var activeFilter = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion>
            {
                new("IsActive", FilterOperator.Equals, "true"),
            });

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.ActiveFilter, activeFilter));

        var valueSelect = cut.Find("select.stratum-filter-builder__value-input");
        valueSelect.Should().NotBeNull();
        valueSelect.OuterHtml.Should().Contain("Oui");
        valueSelect.OuterHtml.Should().Contain("Non");
    }

    [Fact]
    public void ShouldFireOnApplyWithCorrectFilterWhenCriteriaSet()
    {
        FilterGroup? appliedFilter = null;
        var applied = false;

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.OnApply, EventCallback.Factory.Create<FilterGroup?>(this, f =>
            {
                appliedFilter = f;
                applied = true;
            })));

        // 1. Add a criterion row
        cut.Find("[aria-label='Ajouter un critère']").Click();

        // 2. Select field "Name"
        var fieldSelect = cut.Find(".stratum-filter-builder__field-select");
        fieldSelect.Change("Name");

        // 3. Change operator to Contains
        var operatorSelect = cut.Find(".stratum-filter-builder__operator-select");
        operatorSelect.Change("Contains");

        // 4. Enter a value
        var valueInput = cut.Find(".stratum-filter-builder__value-input");
        valueInput.Change("Dupont");

        // 5. Click Appliquer
        cut.Find("button.btn-primary").Click();

        // 6. Verify
        applied.Should().BeTrue("OnApply should have been fired");
        appliedFilter.Should().NotBeNull("filter should not be null when a criterion with field+value is set");
        appliedFilter!.Criteria.Should().HaveCount(1);
        appliedFilter.Criteria[0].Field.Should().Be("Name");
        appliedFilter.Criteria[0].Operator.Should().Be(FilterOperator.Contains);
        appliedFilter.Criteria[0].Value.Should().Be("Dupont");
    }

    [Fact]
    public void ShouldRenderNestedGroupsFromActiveFilter()
    {
        var activeFilter = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion> { new("Name", FilterOperator.Equals, "A") },
            new List<FilterGroup>
            {
                new(
                    FilterLogic.Or,
                    new List<FilterCriterion>
                    {
                        new("Amount", FilterOperator.GreaterThan, 100m),
                    }),
            });

        var cut = Render<StratumFilterBuilder<TestDto>>(p => p
            .Add(f => f.Visible, true)
            .Add(f => f.Registry, _registry)
            .Add(f => f.ActiveFilter, activeFilter));

        cut.FindAll(".stratum-filter-builder__group--nested").Count.Should().Be(1);
    }

    private sealed class TestDto
    {
        public string Name { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public DateTime? DueDate { get; set; }

        public bool IsActive { get; set; }
    }

    private sealed class TestColumnRegistry : IColumnRegistry<TestDto>
    {
        private static readonly List<ColumnDefinition> Columns =
        [
            new("Name", "Nom", "Test", "Name", ColumnDataType.Text, true, "Main", 1),
            new("Amount", "Montant", "Test", "Amount", ColumnDataType.Money, true, "Main", 2),
            new("DueDate", "Échéance", "Test", "DueDate", ColumnDataType.Date, true, "Main", 3),
            new("IsActive", "Actif", "Test", "IsActive", ColumnDataType.Boolean, true, "Main", 4),
        ];

        public IReadOnlyList<ColumnDefinition> GetAvailableColumns() => Columns;

        public IReadOnlyDictionary<string, IReadOnlyList<ColumnDefinition>> GetColumnsByCategory()
            => Columns.GroupBy(c => c.Category)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ColumnDefinition>)g.OrderBy(c => c.SortOrder).ToList());

        public IReadOnlyList<ColumnDefinition> GetDefaultVisibleColumns()
            => Columns.Where(c => c.DefaultVisible).ToList();

        public ColumnDefinition? GetColumn(string key)
            => Columns.FirstOrDefault(c => c.Key == key);

        public IReadOnlyList<string> GetSearchableFields(IReadOnlyList<string>? visibleKeys)
            => (visibleKeys ?? Columns.Where(c => c.DefaultVisible).Select(c => c.Key).ToList())
                .Where(k => GetColumn(k) is { DataType: ColumnDataType.Text or ColumnDataType.Enum or ColumnDataType.Number or ColumnDataType.Money })
                .ToList();
    }

    private sealed class FakeSavedFilterService : ISavedFilterService
    {
        private readonly List<SavedFilter> _filters;

        public FakeSavedFilterService(params SavedFilter[] filters)
        {
            _filters = filters.ToList();
        }

        public int ListCallCount { get; private set; }

        public int SaveCallCount { get; private set; }

        public int DeleteCallCount { get; private set; }

        public int SetDefaultCallCount { get; private set; }

        public SavedFilter? LastSaved { get; private set; }

        public Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default)
        {
            ListCallCount++;
            return Task.FromResult<IReadOnlyList<SavedFilter>>(_filters);
        }

        public Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_filters.FirstOrDefault(f => f.Id == id));

        public Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default)
        {
            SaveCallCount++;
            LastSaved = filter;

            // Mimic upsert semantics of PostgresSavedFilterService.
            var existing = _filters.FindIndex(f => f.Id == filter.Id);
            if (existing >= 0)
            {
                _filters[existing] = filter;
            }
            else
            {
                _filters.Add(filter);
            }

            return Task.FromResult(filter);
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            DeleteCallCount++;
            _filters.RemoveAll(f => f.Id == id);
            return Task.CompletedTask;
        }

        public Task SetDefaultAsync(Guid id, CancellationToken ct = default)
        {
            SetDefaultCallCount++;
            return Task.CompletedTask;
        }
    }
}

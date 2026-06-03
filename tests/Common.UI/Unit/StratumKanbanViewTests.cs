namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class StratumKanbanViewTests : BunitContext
{
    public StratumKanbanViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ── Rendering states ────────────────────────────────────────────
    [Fact]
    public void ShouldRenderEmptyStateWhenDataIsEmpty()
    {
        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, Array.Empty<TaskItem>()));

        cut.Find(".stratum-kanban-view__empty").Should().NotBeNull();
        cut.Find(".stratum-kanban-view__empty").GetAttribute("role").Should().Be("status");
    }

    [Fact]
    public void ShouldRenderLoadingSkeletonWhenLoadingIsTrue()
    {
        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Loading, true));

        var columns = cut.Find(".stratum-kanban-view__columns");
        columns.GetAttribute("aria-busy").Should().Be("true");
        columns.GetAttribute("role").Should().Be("status");

        cut.FindAll(".stratum-kanban-view__column--skeleton").Count.Should().Be(3);
    }

    [Fact]
    public void ShouldRenderCustomSkeletonCardCount()
    {
        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Loading, true)
            .Add(v => v.SkeletonCardCount, 5));

        // 3 skeleton columns × 5 cards = 15
        cut.FindAll(".stratum-kanban-view__card--skeleton").Count.Should().Be(15);
    }

    // ── Grouping ────────────────────────────────────────────────────
    [Fact]
    public void ShouldGroupItemsByProperty()
    {
        var data = new List<TaskItem>
        {
            new(1, "Task A", "Open"),
            new(2, "Task B", "Closed"),
            new(3, "Task C", "Open"),
            new(4, "Task D", "Closed"),
        };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.GroupByProperty, "Status"));

        var columns = cut.FindAll("[role='group']");
        columns.Should().HaveCount(2);

        // First column: Open (2 items)
        var firstTitle = cut.Find("[data-testid='kanban-column-0'] .stratum-kanban-view__column-title");
        firstTitle.TextContent.Should().Be("Open");
        var firstCount = cut.Find("[data-testid='kanban-column-0'] .stratum-kanban-view__column-count");
        firstCount.TextContent.Trim().Should().Be("2");

        // Second column: Closed (2 items)
        var secondTitle = cut.Find("[data-testid='kanban-column-1'] .stratum-kanban-view__column-title");
        secondTitle.TextContent.Should().Be("Closed");
    }

    [Fact]
    public void ShouldShowAllItemsInOneColumnWhenNoGroupByProperty()
    {
        var data = new List<TaskItem>
        {
            new(1, "Task A", "Open"),
            new(2, "Task B", "Closed"),
        };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data));

        var columns = cut.FindAll("[role='group']");
        columns.Should().HaveCount(1);
        cut.Find(".stratum-kanban-view__column-title").TextContent.Should().Be("Tous");
    }

    [Fact]
    public void ShouldHandleNullGroupValues()
    {
        var data = new List<TaskItem>
        {
            new(1, "Task A", null!),
            new(2, "Task B", "Open"),
        };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.GroupByProperty, "Status"));

        var firstTitle = cut.Find("[data-testid='kanban-column-0'] .stratum-kanban-view__column-title");
        firstTitle.TextContent.Should().Be("(vide)");
    }

    // ── Card rendering ────────────────────────────────────────────
    [Fact]
    public void ShouldRenderCardTitleFromColumnRegistry()
    {
        var data = new List<TaskItem> { new(1, "My Task", "Open") };
        var registry = new TaskColumnRegistry();

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.GroupByProperty, "Status")
            .Add(v => v.ColumnRegistry, registry));

        cut.Find(".stratum-kanban-view__card-title").TextContent.Should().Contain("My Task");
    }

    // ── Selection ────────────────────────────────────────────────
    [Fact]
    public void ShouldRenderCheckboxesWhenSelectionAllowed()
    {
        var data = new List<TaskItem> { new(1, "Task", "Open") };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.GroupByProperty, "Status")
            .Add(v => v.AllowSelection, true));

        cut.FindAll(".stratum-kanban-view__checkbox input[type='checkbox']").Should().HaveCount(1);
    }

    [Fact]
    public void ShouldMarkSelectedCardsWithAriaSelected()
    {
        var data = new List<TaskItem>
        {
            new(1, "Task A", "Open"),
            new(2, "Task B", "Open"),
        };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.GroupByProperty, "Status")
            .Add(v => v.AllowSelection, true)
            .Add(v => v.SelectedItems, new List<TaskItem> { data[0] }));

        cut.Find("[data-testid='kanban-card-0-0']").GetAttribute("aria-selected").Should().Be("true");
        cut.Find("[data-testid='kanban-card-0-1']").GetAttribute("aria-selected").Should().Be("false");
    }

    // ── Keyboard navigation ──────────────────────────────────────
    [Fact]
    public async Task EnterShouldFireOnRowActivated()
    {
        var activatedItem = (TaskItem?)null;
        var data = new List<TaskItem>
        {
            new(1, "Task A", "Open"),
            new(2, "Task B", "Open"),
        };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.GroupByProperty, "Status")
            .Add(v => v.OnRowActivated, EventCallback.Factory.Create<TaskItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-kanban-view");
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem.Should().NotBeNull();
        activatedItem!.Id.Should().Be(1);
    }

    [Fact]
    public async Task ArrowDownThenEnterShouldActivateSecondCard()
    {
        var activatedItem = (TaskItem?)null;
        var data = new List<TaskItem>
        {
            new(1, "Task A", "Open"),
            new(2, "Task B", "Open"),
        };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.GroupByProperty, "Status")
            .Add(v => v.OnRowActivated, EventCallback.Factory.Create<TaskItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-kanban-view");
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem!.Id.Should().Be(2);
    }

    [Fact]
    public async Task ArrowRightShouldMoveToNextColumn()
    {
        var activatedItem = (TaskItem?)null;
        var data = new List<TaskItem>
        {
            new(1, "Task A", "Open"),
            new(2, "Task B", "Closed"),
        };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.GroupByProperty, "Status")
            .Add(v => v.OnRowActivated, EventCallback.Factory.Create<TaskItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-kanban-view");
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem!.Id.Should().Be(2);
    }

    [Fact]
    public async Task SpaceShouldToggleSelectionWhenAllowed()
    {
        IReadOnlyList<TaskItem>? selected = null;
        var data = new List<TaskItem> { new(1, "Task A", "Open") };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.GroupByProperty, "Status")
            .Add(v => v.AllowSelection, true)
            .Add(v => v.OnSelectionChanged, EventCallback.Factory.Create<IReadOnlyList<TaskItem>>(this, items => selected = items)));

        var wrapper = cut.Find(".stratum-kanban-view");
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = " " });

        selected.Should().NotBeNull();
        selected!.Count.Should().Be(1);
    }

    // ── ARIA roles ───────────────────────────────────────────────
    [Fact]
    public void ColumnsShouldHaveGroupRole()
    {
        var data = new List<TaskItem> { new(1, "Task", "Open") };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.GroupByProperty, "Status")
            .Add(v => v.AriaLabel, "Kanban board"));

        var column = cut.Find("[role='group']");
        column.GetAttribute("aria-label").Should().Be("Open");

        cut.FindAll("[role='listitem']").Should().HaveCount(1);
    }

    // ── TestId ───────────────────────────────────────────────────
    [Fact]
    public void ShouldUseDefaultTestId()
    {
        var data = new List<TaskItem> { new(1, "Task", "Open") };

        var cut = Render<StratumKanbanView<TaskItem>>(p => p
            .Add(v => v.Data, data));

        cut.Find("[data-testid='stratum-kanban-view']").Should().NotBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────
    private sealed record TaskItem(int Id, string Name, string? Status);

    private sealed class TaskColumnRegistry : ColumnRegistryBase<TaskItem>
    {
        protected override void Configure()
        {
            Column("Name", "Nom", "Task", defaultVisible: true, sortOrder: 1);
            Column("Status", "Statut", "Task", defaultVisible: true, sortOrder: 2);
        }
    }
}

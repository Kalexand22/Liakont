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

public sealed class StratumListContainerTests : BunitContext
{
    public StratumListContainerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // FilterBar needs IShortcutService; FilterBuilder needs ISavedFilterService
        Services.AddSingleton<IShortcutService, StubShortcutService>();
        Services.AddSingleton<ISavedFilterService, StubSavedFilterService>();
    }

    // ── Basic rendering ─────────────────────────────────────────────
    [Fact]
    public void ShouldRenderContainerWithToolbar()
    {
        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Title, "Products")
            .Add(c => c.Data, SampleData()));

        cut.Find(".stratum-list-container").Should().NotBeNull();
        cut.Find("[role='toolbar']").Should().NotBeNull();
        cut.Find(".stratum-list-container__title").TextContent.Should().Contain("Products");
    }

    [Fact]
    public void ShouldRenderTotalCount()
    {
        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Title, "Products")
            .Add(c => c.TotalCount, 42)
            .Add(c => c.Data, SampleData()));

        cut.Find(".stratum-list-container__count").TextContent.Should().Contain("42");
    }

    // ── View switching ──────────────────────────────────────────────
    [Fact]
    public void ShouldRenderViewSwitcherWhenMultipleViewsConfigured()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .Build();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ViewModes, registry));

        var buttons = cut.FindAll("[role='radio']");
        buttons.Should().HaveCount(2);
    }

    [Fact]
    public void ShouldNotRenderViewSwitcherWithSingleView()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .Build();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ViewModes, registry));

        cut.FindAll(".stratum-list-container__view-switcher").Should().BeEmpty();
    }

    [Fact]
    public async Task SwitchingViewShouldFireOnViewChanged()
    {
        ViewKind? changedTo = null;
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .Build();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ViewModes, registry)
            .Add(c => c.OnViewChanged, EventCallback.Factory.Create<ViewKind>(this, kind => changedTo = kind)));

        await cut.Find("[data-testid='view-switch-card']").ClickAsync(new MouseEventArgs());

        changedTo.Should().Be(ViewKind.Card);
    }

    [Fact]
    public async Task SwitchingViewShouldRenderCardView()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .Build();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ViewModes, registry));

        // Initially table view (no card view rendered)
        cut.FindAll("[data-testid='stratum-card-view']").Should().BeEmpty();

        await cut.Find("[data-testid='view-switch-card']").ClickAsync(new MouseEventArgs());

        cut.Find("[data-testid='stratum-card-view']").Should().NotBeNull();
    }

    [Fact]
    public async Task SwitchingViewShouldRenderKanbanView()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddKanban("Status")
            .Build();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ViewModes, registry)
            .Add(c => c.KanbanGroupByProperty, "Category"));

        await cut.Find("[data-testid='view-switch-kanban']").ClickAsync(new MouseEventArgs());

        cut.Find("[data-testid='stratum-kanban-view']").Should().NotBeNull();
    }

    [Fact]
    public async Task SwitchingViewShouldRenderCalendarView()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCalendar("DueDate")
            .Build();

        var data = new List<TestItem>
        {
            new(1, "Item 1", "A", 10m),
        };

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, data)
            .Add(c => c.ViewModes, registry)
            .Add(c => c.CalendarDateProperty, "DueDate"));

        await cut.Find("[data-testid='view-switch-calendar']").ClickAsync(new MouseEventArgs());

        cut.Find("[data-testid='stratum-calendar-view']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldUseInitialViewWhenProvided()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .Build();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ViewModes, registry)
            .Add(c => c.InitialView, ViewKind.Card));

        // Card view should be active
        cut.Find("[data-testid='stratum-card-view']").Should().NotBeNull();

        // Card button should be checked
        cut.Find("[data-testid='view-switch-card']").GetAttribute("aria-checked").Should().Be("true");
    }

    [Fact]
    public void ShouldDefaultToTableViewWhenNoInitialView()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .Build();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ViewModes, registry));

        // Table should be active
        cut.Find("[data-testid='view-switch-table']").GetAttribute("aria-checked").Should().Be("true");
    }

    // ── View switcher ARIA ──────────────────────────────────────────
    [Fact]
    public void ViewSwitcherShouldHaveRadiogroupRole()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .Build();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ViewModes, registry));

        var switcher = cut.Find("[role='radiogroup']");
        switcher.GetAttribute("aria-label").Should().Be("Mode d'affichage");
    }

    // ── Column chooser button ───────────────────────────────────────
    [Fact]
    public void ShouldShowColumnChooserButtonInTableView()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .Build();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ViewModes, registry));

        // Table is default view — column chooser should be visible
        cut.Find("[data-testid='btn-column-chooser']").Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldHideColumnChooserButtonInCardView()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .Build();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ViewModes, registry));

        await cut.Find("[data-testid='view-switch-card']").ClickAsync(new MouseEventArgs());

        cut.FindAll("[data-testid='btn-column-chooser']").Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldOpenColumnChooserDialogOnClick()
    {
        var columnRegistry = new TestItemColumnRegistry();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ColumnRegistry, columnRegistry));

        // Before click — no dialog
        cut.FindAll("[data-testid='stratum-column-chooser']").Should().BeEmpty();

        // Click "Colonnes" button
        await cut.Find("[data-testid='btn-column-chooser']").ClickAsync(new MouseEventArgs());

        // Dialog should now be visible
        cut.Find("[data-testid='stratum-column-chooser']").Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldApplyColumnsFromChooser()
    {
        var columnRegistry = new TestItemColumnRegistry();
        IReadOnlyList<string>? appliedKeys = null;

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ColumnRegistry, columnRegistry)
            .Add(c => c.OnColumnsApplied, (IReadOnlyList<string> keys) => { appliedKeys = keys; }));

        // Open the chooser
        await cut.Find("[data-testid='btn-column-chooser']").ClickAsync(new MouseEventArgs());

        // Click "Appliquer"
        var applyBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Appliquer");
        await applyBtn.ClickAsync(new MouseEventArgs());

        // Should have received column keys
        appliedKeys.Should().NotBeNull();
        appliedKeys!.Count.Should().BeGreaterThan(0);

        // Dialog should be closed
        cut.FindAll("[data-testid='stratum-column-chooser']").Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldResetColumnsToDefault()
    {
        var columnRegistry = new TestItemColumnRegistry();

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ColumnRegistry, columnRegistry)
            .Add(c => c.VisibleColumnKeys, new List<string> { "Name" }));

        // Open the chooser
        await cut.Find("[data-testid='btn-column-chooser']").ClickAsync(new MouseEventArgs());

        // Click "Réinitialiser"
        var resetBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Réinitialiser");
        await resetBtn.ClickAsync(new MouseEventArgs());

        // Verify that all default visible columns are restored in the order list
        var orderItems = cut.FindAll(".stratum-column-chooser__order-item");
        orderItems.Count.Should().Be(columnRegistry.GetDefaultVisibleColumns().Count);
    }

    // ── Filter builder button ───────────────────────────────────────
    [Fact]
    public void ShouldShowFilterBuilderWhenEnabled()
    {
        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ShowFilterBuilder, true));

        cut.Find("[data-testid='btn-filter-builder']").Should().NotBeNull();
    }

    // ── Actions in toolbar ──────────────────────────────────────────
    [Fact]
    public void ShouldRenderActionsInToolbar()
    {
        var actions = new List<GridAction>
        {
            new("create", "Nouveau", IsPrimary: true),
            new("export", "Exporter"),
        };

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.Actions, actions));

        cut.Find("[data-testid='action-create']").Should().NotBeNull();
        cut.Find("[data-testid='action-export']").Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldRenderActionGroupsInToolbar()
    {
        var groups = new List<GridActionGroup>
        {
            new("Bulk", null, new List<GridAction>
            {
                new("archive", "Archiver"),
                new("assign", "Assigner"),
            }),
        };

        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ActionGroups, groups));

        var groupToggle = cut.Find("[data-testid='action-group-0']");
        groupToggle.TextContent.Should().Contain("Bulk");

        // Open the dropdown to reveal menu items
        await groupToggle.ClickAsync(new MouseEventArgs());
        cut.FindAll("[role='menuitem']").Should().HaveCount(2);
    }

    // ── Footer / Error content ──────────────────────────────────────
    [Fact]
    public void ShouldRenderFooterContent()
    {
        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.FooterContent, builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "custom-footer");
                builder.AddContent(2, "Page 1 of 5");
                builder.CloseElement();
            }));

        cut.Find(".custom-footer").TextContent.Should().Be("Page 1 of 5");
    }

    [Fact]
    public void ShouldRenderErrorContent()
    {
        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.ErrorContent, builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "error-msg");
                builder.AddContent(2, "Something went wrong");
                builder.CloseElement();
            }));

        cut.Find(".error-msg").TextContent.Should().Be("Something went wrong");
    }

    // ── TestId ───────────────────────────────────────────────────
    [Fact]
    public void ShouldUseDefaultTestId()
    {
        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData()));

        cut.Find("[data-testid='stratum-list-container']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldUseCustomTestId()
    {
        var cut = Render<StratumListContainer<TestItem>>(p => p
            .Add(c => c.Data, SampleData())
            .Add(c => c.TestId, "my-container"));

        cut.Find("[data-testid='my-container']").Should().NotBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────
    private static List<TestItem> SampleData(int count = 3) =>
        Enumerable.Range(1, count)
            .Select(i => new TestItem(i, $"Item {i}", i % 2 == 0 ? "B" : "A", i * 10m))
            .ToList();

    private sealed record TestItem(int Id, string Name, string Category, decimal Price);

    private sealed class TestItemColumnRegistry : IColumnRegistry<TestItem>
    {
        private static readonly List<ColumnDefinition> Columns =
        [
            new("Name", "Nom", "TestItem", "Name", ColumnDataType.Text, true, "Main", 0),
            new("Category", "Catégorie", "TestItem", "Category", ColumnDataType.Text, true, "Main", 1),
            new("Price", "Prix", "TestItem", "Price", ColumnDataType.Money, true, "Main", 2),
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
            => ["Name", "Category"];
    }

    /// <summary>Stub for IShortcutService required by FilterBar.</summary>
    private sealed class StubShortcutService : IShortcutService
    {
#pragma warning disable CS0067 // Event never used
        public event Action? ScopeChanged;
#pragma warning restore CS0067

        public void PushScope(string scopeId, ShortcutScopeType scopeType, IReadOnlyList<ScopeBinding> bindings)
        {
        }

        public void PopScope(string scopeId)
        {
        }

        public IReadOnlyDictionary<string, string> ComputeActiveBindings()
        {
            return new Dictionary<string, string>();
        }

        public Task ExecuteCommandAsync(string commandId)
        {
            return Task.CompletedTask;
        }

        public IReadOnlyList<CommandGroup> GetVisibleCommands(ICommandRegistry registry)
        {
            return Array.Empty<CommandGroup>();
        }
    }

    /// <summary>Stub for ISavedFilterService required by StratumFilterBuilder.</summary>
    private sealed class StubSavedFilterService : ISavedFilterService
    {
        public Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SavedFilter>>(Array.Empty<SavedFilter>());

        public Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<SavedFilter?>(null);

        public Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default)
            => Task.FromResult(filter);

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetDefaultAsync(Guid id, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

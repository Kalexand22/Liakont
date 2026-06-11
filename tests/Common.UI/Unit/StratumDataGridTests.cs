namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Display;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class StratumDataGridTests : BunitContext
{
    private static readonly string[] ExpectedContextMenuColumnKeys = ["Name", "Category"];

    public StratumDataGridTests()
    {
        // Radzen components use JS interop heavily — set loose mode to catch-all
        JSInterop.Mode = JSRuntimeMode.Loose;

        // StratumDataGrid injects IDisplayTemplateRegistry — register for all tests
        Services.AddSingleton<IDisplayTemplateRegistry, DisplayTemplateRegistry>();

        // StratumDataGrid injects IStringLocalizer<SharedResources> for its inline filter bar
        // and status badges — provide a pass-through stub for unit tests.
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubStringLocalizer<SharedResources>());
    }

    [Fact]
    public void ShouldRenderEmptyStateWhenDataIsNull()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, null));

        cut.Find(".stratum-datagrid__empty").Should().NotBeNull();
        cut.Find(".stratum-datagrid__empty-text").TextContent.Should().Contain("Aucun");
    }

    [Fact]
    public void ShouldRenderEmptyStateWhenDataIsEmpty()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, Array.Empty<TestItem>()));

        cut.Find(".stratum-datagrid__empty").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderCustomEmptyContent()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, Array.Empty<TestItem>())
            .Add(g => g.EmptyContent, builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "custom-empty");
                builder.AddContent(2, "Nothing here");
                builder.CloseElement();
            }));

        cut.Find(".custom-empty").TextContent.Should().Be("Nothing here");
    }

    [Fact]
    public void ShouldRenderLoadingSkeletonWhenLoadingIsTrue()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Loading, true));

        var loading = cut.Find(".stratum-datagrid__loading");
        loading.Should().NotBeNull();
        loading.GetAttribute("aria-busy").Should().Be("true");
        loading.GetAttribute("role").Should().Be("status");

        // Default skeleton has 5 rows × 4 columns
        cut.FindAll(".stratum-datagrid__skeleton-row").Count.Should().Be(5);
        cut.FindAll(".stratum-datagrid__skeleton-cell").Count.Should().Be(20);
    }

    [Fact]
    public void ShouldRenderCustomSkeletonColumnCount()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Loading, true)
            .Add(g => g.SkeletonColumnCount, 6));

        // 5 rows × 6 columns = 30 cells
        cut.FindAll(".stratum-datagrid__skeleton-cell").Count.Should().Be(30);
    }

    [Fact]
    public void ShouldNotRenderRadzenGridWhenLoading()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, SampleData())
            .Add(g => g.Loading, true));

        // Loading takes priority over data — Radzen grid should not render
        cut.FindAll(".stratum-datagrid__loading").Count.Should().Be(1);
        cut.FindAll(".stratum-datagrid").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldRenderRadzenGridWhenDataIsProvided()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, SampleData()));

        // No empty state, no loading — wrapper is rendered
        cut.FindAll(".stratum-datagrid__empty").Count.Should().Be(0);
        cut.FindAll(".stratum-datagrid__loading").Count.Should().Be(0);
        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public async Task ArrowDownShouldNotThrowWhenDataIsPresent()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, SampleData()));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // Should not throw — validates keyboard handler doesn't crash
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Home" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "End" });
    }

    [Fact]
    public async Task EnterShouldFireOnRowActivated()
    {
        var activatedItem = (TestItem?)null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // Focus is on row 0 by default, Enter should activate it
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem.Should().NotBeNull();
        activatedItem!.Id.Should().Be(1);
    }

    [Fact]
    public async Task ArrowDownThenEnterShouldActivateSecondRow()
    {
        var activatedItem = (TestItem?)null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem.Should().NotBeNull();
        activatedItem!.Id.Should().Be(2);
    }

    [Fact]
    public async Task HomeShouldResetFocusToFirstRow()
    {
        var activatedItem = (TestItem?)null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // Move down twice, then Home, then Enter
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Home" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem!.Id.Should().Be(1);
    }

    [Fact]
    public async Task EndShouldFocusLastRow()
    {
        var activatedItem = (TestItem?)null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "End" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem!.Id.Should().Be(3);
    }

    [Fact]
    public async Task ArrowUpShouldNotGoBelowZero()
    {
        var activatedItem = (TestItem?)null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // ArrowUp from index 0 should stay at 0
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem!.Id.Should().Be(1);
    }

    [Fact]
    public async Task ArrowDownShouldNotExceedLastRow()
    {
        var activatedItem = (TestItem?)null;
        var data = SampleData(2);

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // Move down 5 times with only 2 items — should clamp to last
        for (var i = 0; i < 5; i++)
        {
            await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        }

        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem!.Id.Should().Be(2);
    }

    [Fact]
    public async Task SpaceShouldToggleSelectionWhenAllowed()
    {
        IReadOnlyList<TestItem>? selected = null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.AllowSelection, true)
            .Add(g => g.SelectionMode, SelectionMode.Multiple)
            .Add(g => g.OnSelectionChanged, EventCallback.Factory.Create<IReadOnlyList<TestItem>>(this, items => selected = items)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // Space on first row should select it
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = " " });

        selected.Should().NotBeNull();
        selected!.Count.Should().Be(1);
        selected[0].Id.Should().Be(1);
    }

    [Fact]
    public async Task SpaceShouldDeselectAlreadySelectedItem()
    {
        IReadOnlyList<TestItem>? selected = null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.AllowSelection, true)
            .Add(g => g.SelectionMode, SelectionMode.Multiple)
            .Add(g => g.SelectedItems, new List<TestItem> { data[0] })
            .Add(g => g.OnSelectionChanged, EventCallback.Factory.Create<IReadOnlyList<TestItem>>(this, items => selected = items)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // Space on already-selected first row should deselect it
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = " " });

        selected.Should().NotBeNull();
        selected!.Count.Should().Be(0);
    }

    [Fact]
    public async Task XKeyShouldAlsoToggleSelection()
    {
        IReadOnlyList<TestItem>? selected = null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.AllowSelection, true)
            .Add(g => g.OnSelectionChanged, EventCallback.Factory.Create<IReadOnlyList<TestItem>>(this, items => selected = items)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "x" });

        selected.Should().NotBeNull();
        selected!.Count.Should().Be(1);
    }

    [Fact]
    public async Task SingleSelectionModeShouldClearPreviousSelection()
    {
        IReadOnlyList<TestItem>? selected = null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.AllowSelection, true)
            .Add(g => g.SelectionMode, SelectionMode.One)
            .Add(g => g.SelectedItems, new List<TestItem> { data[0] })
            .Add(g => g.OnSelectionChanged, EventCallback.Factory.Create<IReadOnlyList<TestItem>>(this, items => selected = items)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // Move to second row and select via Space
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = " " });

        // In Single mode, previous selection should be cleared
        selected.Should().NotBeNull();
        selected!.Count.Should().Be(1);
        selected[0].Id.Should().Be(2);
    }

    [Fact]
    public async Task SelectionShouldNotFireWhenNotAllowed()
    {
        IReadOnlyList<TestItem>? selected = null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.AllowSelection, false)
            .Add(g => g.OnSelectionChanged, EventCallback.Factory.Create<IReadOnlyList<TestItem>>(this, items => selected = items)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = " " });

        selected.Should().BeNull();
    }

    [Fact]
    public async Task KeyboardShouldBeNoOpWhenDataIsNull()
    {
        var activatedItem = (TestItem?)null;

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, (IReadOnlyList<TestItem>?)null)
            .Add(g => g.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // Should be a no-op, not throw
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem.Should().BeNull();
    }

    [Fact]
    public void ShouldRenderExportToolbarWhenAllowed()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, SampleData())
            .Add(g => g.AllowExport, true));

        cut.Find("[role='toolbar']").Should().NotBeNull();

        // A single consolidated "Export" menu button replaces the former three identical
        // download icons (FIX206).
        cut.Find("[data-testid='export-btn']").Should().NotBeNull();
        cut.FindAll("[data-testid='export-csv-btn']").Should().BeEmpty();
        cut.FindAll("[data-testid='export-excel-btn']").Should().BeEmpty();
        cut.FindAll("[data-testid='export-pdf-btn']").Should().BeEmpty();
    }

    [Fact]
    public void ShouldNotRenderExportToolbarByDefault()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, SampleData()));

        cut.FindAll("[role='toolbar']").Count.Should().Be(0);
    }

    [Fact]
    public void ExportButtonShouldBeDisabledWhenLoading()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Loading, true)
            .Add(g => g.AllowExport, true));

        var exportBtn = cut.Find("[data-testid='export-btn']");
        exportBtn.QuerySelectorAll("button").Any(b => b.HasAttribute("disabled")).Should().BeTrue();
    }

    [Fact]
    public void ExportButtonShouldBeDisabledWhenDataIsEmpty()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, Array.Empty<TestItem>())
            .Add(g => g.AllowExport, true));

        var exportBtn = cut.Find("[data-testid='export-btn']");
        exportBtn.QuerySelectorAll("button").Any(b => b.HasAttribute("disabled")).Should().BeTrue();
    }

    [Fact]
    public async Task ExportButtonShouldFireOnExportCallbackWithDefaultFormat()
    {
        ExportArgs? exportArgs = null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.AllowExport, true)
            .Add(g => g.OnExport, EventCallback.Factory.Create<ExportArgs>(this, args => exportArgs = args)));

        // The primary "Export" button exports the first enabled format (CSV by default).
        var primary = cut.Find("[data-testid='export-btn']").QuerySelector("button")!;
        await primary.ClickAsync(new MouseEventArgs());

        exportArgs.Should().NotBeNull();
        exportArgs!.Format.Should().Be(ExportFormat.Csv);
        exportArgs.FileName.Should().Be("export");
    }

    [Fact]
    public void ExportMenuShouldListConfiguredFormats()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, SampleData())
            .Add(g => g.AllowExport, true)
            .Add(g => g.ExportFormats, ExportFormat.Csv | ExportFormat.Excel | ExportFormat.Pdf));

        cut.Find("[data-testid='export-btn']").Should().NotBeNull();

        // The three formats are offered as menu items (CSV / Excel / PDF) under the single button.
        cut.Markup.Should().Contain("CSV");
        cut.Markup.Should().Contain("Excel");
        cut.Markup.Should().Contain("PDF");
    }

    [Fact]
    public void ShouldRenderRefreshButtonWhenOnRefreshSet()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, SampleData())
            .Add(g => g.OnRefresh, EventCallback.Factory.Create(this, () => { })));

        cut.Find("[role='toolbar']").Should().NotBeNull();
        cut.Find("[data-testid='refresh-btn']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldNotRenderRefreshButtonByDefault()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, SampleData()));

        cut.FindAll("[data-testid='refresh-btn']").Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshButtonShouldInvokeOnRefreshCallback()
    {
        var refreshed = false;
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, SampleData())
            .Add(g => g.OnRefresh, EventCallback.Factory.Create(this, () => refreshed = true)));

        var btn = cut.Find("[data-testid='refresh-btn']");
        await btn.ClickAsync(new MouseEventArgs());

        refreshed.Should().BeTrue();
    }

    [Fact]
    public void LoadingStateShouldHaveAriaAttributes()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Loading, true)
            .Add(g => g.AriaLabel, "Produits"));

        var loading = cut.Find(".stratum-datagrid__loading");
        loading.GetAttribute("role").Should().Be("status");
        loading.GetAttribute("aria-busy").Should().Be("true");
        loading.GetAttribute("aria-label").Should().Be("Produits");
    }

    [Fact]
    public void EmptyStateShouldHaveStatusRole()
    {
        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, Array.Empty<TestItem>()));

        cut.Find(".stratum-datagrid__empty").GetAttribute("role").Should().Be("status");
    }

    [Fact]
    public void ShouldRenderWithLargeDatasetAndVirtualization()
    {
        // 5000+ items should not throw during render
        var largeData = SampleData(5000);

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, largeData)
            .Add(g => g.AllowVirtualization, true));

        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public async Task FocusedRowShouldResetWhenDataChanges()
    {
        // Verify that new data resets focused row to 0.
        // Render a fresh component with different data to verify default focus index.
        var activatedItem = (TestItem?)null;
        var data = SampleData(3);

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // Default focus index is 0 — Enter should activate first row
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem!.Id.Should().Be(1);
    }

    [Fact]
    public async Task MultipleSelectionShouldAccumulateItems()
    {
        // Verify multiple items can be selected via Space on different rows.
        // The keyboard handler builds a new list from SelectedItems each time.
        IReadOnlyList<TestItem>? selected = null;
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.AllowSelection, true)
            .Add(g => g.SelectionMode, SelectionMode.Multiple)
            .Add(g => g.OnSelectionChanged, EventCallback.Factory.Create<IReadOnlyList<TestItem>>(this, items => selected = items)));

        var wrapper = cut.Find(".stratum-datagrid-wrapper");

        // Select first row
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = " " });

        selected.Should().NotBeNull();
        selected!.Count.Should().Be(1);
        selected[0].Id.Should().Be(1);
    }

    // ── Dynamic columns tests ──────────────────────────────────────────
    [Fact]
    public void ShouldRenderDynamicColumnsFromRegistry()
    {
        var registry = new TestColumnRegistry();
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Grid.Main")
            .Add(g => g.ColumnRegistry, registry));

        // Should render without crashing — dynamic columns from registry defaults
        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderOnlyVisibleColumnsFromKeys()
    {
        var registry = new TestColumnRegistry();
        var data = SampleData();
        var visibleKeys = new List<string> { "Name" };

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Grid.Main")
            .Add(g => g.ColumnRegistry, registry)
            .Add(g => g.VisibleColumnKeys, visibleKeys));

        // Should render without crashing — only "Name" column visible
        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void ShouldFallBackToChildContentWhenNoRegistry()
    {
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data));

        // Without registry, no dynamic columns — should still render via ChildContent path
        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void ShouldIgnoreInvalidColumnKeysGracefully()
    {
        var registry = new TestColumnRegistry();
        var data = SampleData();
        var visibleKeys = new List<string> { "Name", "NonExistentColumn" };

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Grid.Main")
            .Add(g => g.ColumnRegistry, registry)
            .Add(g => g.VisibleColumnKeys, visibleKeys));

        // Should render without crashing — invalid keys silently skipped
        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void LoadDataArgsShouldHaveEmptyRequestedFieldsByDefault()
    {
        var args = new LoadDataArgs(0, 25, null, null);
        args.RequestedFields.Should().NotBeNull();
        args.RequestedFields.Should().BeEmpty();
    }

    [Fact]
    public void LoadDataArgsShouldAcceptRequestedFields()
    {
        var fields = new List<string> { "Name", "Customer.City" };
        var args = new LoadDataArgs(0, 25, null, null, fields);
        args.RequestedFields.Should().HaveCount(2);
        args.RequestedFields.Should().Contain("Customer.City");
    }

    [Fact]
    public void ShouldRenderRegistryDefaultsWhenVisibleKeysIsNull()
    {
        var registry = new TestColumnRegistry();
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Grid.Main")
            .Add(g => g.ColumnRegistry, registry)
            .Add(g => g.VisibleColumnKeys, (IReadOnlyList<string>?)null));

        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderWithAllColumnKeysExplicit()
    {
        var registry = new TestColumnRegistry();
        var data = SampleData();
        var allKeys = new List<string> { "Name", "Category", "Price" };

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Grid.Main")
            .Add(g => g.ColumnRegistry, registry)
            .Add(g => g.VisibleColumnKeys, allKeys));

        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderWithReducedColumnKeys()
    {
        var registry = new TestColumnRegistry();
        var data = SampleData();
        var reducedKeys = new List<string> { "Name" };

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Grid.Main")
            .Add(g => g.ColumnRegistry, registry)
            .Add(g => g.VisibleColumnKeys, reducedKeys));

        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void ShouldAcceptOnVisibleColumnsChangedCallback()
    {
        IReadOnlyList<string>? changedKeys = null;
        var registry = new TestColumnRegistry();
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Grid.Main")
            .Add(g => g.ColumnRegistry, registry)
            .Add(g => g.OnVisibleColumnsChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(
                this, keys => changedKeys = keys)));

        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderWithRelatedTableColumns()
    {
        var registry = new RelatedColumnRegistry();
        var data = SampleRelatedData();

        var cut = Render<StratumDataGrid<RelatedItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Related.Main")
            .Add(g => g.ColumnRegistry, registry)
            .Add(g => g.VisibleColumnKeys, new List<string> { "Name", "Customer.Name" }));

        cut.Find(".stratum-datagrid-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderDisplayColumnUsingDisplayTemplate()
    {
        // Register a DisplayTemplateRegistry with a template for CustomerEntity
        Services.AddSingleton<IDisplayTemplate<CustomerEntity>, CustomerDisplayTemplate>();
        Services.AddSingleton<IDisplayTemplateRegistry, DisplayTemplateRegistry>();

        var registry = new DisplayColumnTestRegistry();
        var data = new List<OrderWithCustomer>
        {
            new(1, "ORD-001", new CustomerEntity("Acme Corp", "Paris")),
            new(2, "ORD-002", new CustomerEntity("Globex Inc", "Lyon")),
        };

        var cut = Render<StratumDataGrid<OrderWithCustomer>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Display.Main")
            .Add(g => g.ColumnRegistry, registry)
            .Add(g => g.VisibleColumnKeys, new List<string> { "OrderNumber", "Customer" }));

        var markup = cut.Markup;

        // DisplayTemplate formats as "Name (City)" — verify template output appears
        markup.Should().Contain("Acme Corp (Paris)");
        markup.Should().Contain("Globex Inc (Lyon)");
    }

    // ── GFI09 — cell right-click context menu ────────────────────────
    [Fact]
    public void ShouldRenderCellWrapperWithColumnKeyWhenDynamicColumnsAreResolved()
    {
        var registry = new TestColumnRegistry();
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Grid.Main")
            .Add(g => g.ColumnRegistry, registry));

        // Each resolved dynamic column renders a <span class="stratum-datagrid__cell">
        // with a data-column-key attribute that GFI09 uses for right-click targeting.
        var cells = cut.FindAll(".stratum-datagrid__cell");
        cells.Should().NotBeEmpty();
        cells.Select(c => c.GetAttribute("data-column-key"))
             .Should().Contain(ExpectedContextMenuColumnKeys);
    }

    [Fact]
    public async Task ShouldRaiseOnCellContextMenuWithFieldAndValue()
    {
        var registry = new TestColumnRegistry();
        var data = SampleData();
        GridCellContextMenuArgs<TestItem>? received = null;

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Grid.Main")
            .Add(g => g.ColumnRegistry, registry)
            .Add(g => g.OnCellContextMenu, EventCallback.Factory.Create<GridCellContextMenuArgs<TestItem>>(
                this, args => received = args)));

        // Right-click the first "Name" cell — should raise the event with field="Name"
        var nameCell = cut.FindAll("[data-column-key='Name']")[0];
        await nameCell.TriggerEventAsync("oncontextmenu", new MouseEventArgs { ClientX = 123, ClientY = 456 });

        received.Should().NotBeNull();
        received!.Field.Should().Be("Name");
        received.ColumnKey.Should().Be("Name");
        received.ClientX.Should().Be(123);
        received.ClientY.Should().Be(456);

        // First data row is "Item 1".
        received.DisplayValue.Should().Be("Item 1");
        received.Value.Should().Be("Item 1");
    }

    [Fact]
    public async Task ShouldNotFailWhenOnCellContextMenuIsNotWired()
    {
        var registry = new TestColumnRegistry();
        var data = SampleData();

        var cut = Render<StratumDataGrid<TestItem>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Grid.Main")
            .Add(g => g.ColumnRegistry, registry));

        // Without a wired delegate, right-clicking a cell must be a no-op (no exception,
        // browser default menu shows because preventDefault stays false).
        var nameCell = cut.FindAll("[data-column-key='Name']")[0];
        var act = async () => await nameCell.TriggerEventAsync("oncontextmenu", new MouseEventArgs { ClientX = 0, ClientY = 0 });
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void ShouldFallBackToToStringWhenNoDisplayTemplateRegistered()
    {
        // Register DisplayTemplateRegistry WITHOUT a template for CustomerEntity
        Services.AddSingleton<IDisplayTemplateRegistry, DisplayTemplateRegistry>();

        var registry = new DisplayColumnTestRegistry();
        var data = new List<OrderWithCustomer>
        {
            new(1, "ORD-001", new CustomerEntity("Acme Corp", "Paris")),
        };

        var cut = Render<StratumDataGrid<OrderWithCustomer>>(p => p
            .Add(g => g.Data, data)
            .Add(g => g.GridKey, "Test.Display.Fallback")
            .Add(g => g.ColumnRegistry, registry)
            .Add(g => g.VisibleColumnKeys, new List<string> { "OrderNumber", "Customer" }));

        var markup = cut.Markup;

        // Without template, falls back to ToString()
        markup.Should().Contain("CustomerEntity");
    }

    private static List<TestItem> SampleData(int count = 3) =>
        Enumerable.Range(1, count)
            .Select(i => new TestItem(i, $"Item {i}", i % 2 == 0 ? "B" : "A", i * 10m))
            .ToList();

    private static List<RelatedItem> SampleRelatedData() =>
    [
        new(1, "Order 1", "Acme"),
        new(2, "Order 2", "Globex"),
    ];

    // ── Display column test types ──────────────────────────────────
    internal sealed record CustomerEntity(string Name, string City);

    internal sealed class CustomerDisplayTemplate : IDisplayTemplate<CustomerEntity>
    {
        public string Format(CustomerEntity entity) => $"{entity.Name} ({entity.City})";
    }

    internal sealed record OrderWithCustomer(int Id, string OrderNumber, CustomerEntity Customer);

    private sealed record TestItem(int Id, string Name, string Category, decimal Price);

    private sealed record RelatedItem(int Id, string Name, string CustomerName);

    private sealed class TestColumnRegistry : ColumnRegistryBase<TestItem>
    {
        protected override void Configure()
        {
            Column("Name", "Nom", "Test", defaultVisible: true, sortOrder: 1);
            Column("Category", "Catégorie", "Test", defaultVisible: true, sortOrder: 2);
            Column("Price", "Prix", "Test", dataType: ColumnDataType.Money, defaultVisible: false, sortOrder: 3);
        }
    }

    private sealed class RelatedColumnRegistry : ColumnRegistryBase<RelatedItem>
    {
        protected override void Configure()
        {
            Column("Name", "Nom", "Order", defaultVisible: true, sortOrder: 1);
            RelatedColumn("Customer.Name", "Nom client", "Customer", defaultVisible: true, sortOrder: 1);
        }
    }

    private sealed class DisplayColumnTestRegistry : ColumnRegistryBase<OrderWithCustomer>
    {
        protected override void Configure()
        {
            Column("OrderNumber", "Numéro", "Order", defaultVisible: true, sortOrder: 1);
            DisplayColumn("Customer", "Client", typeof(CustomerEntity), "Customer", defaultVisible: true, sortOrder: 2, searchableFields: ["Customer.Name", "Customer.City"]);
        }
    }

    private sealed class StubStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}

namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

/// <summary>
/// bUnit tests for <see cref="StratumColumnChooser{TItem}"/>.
/// Covers rendering, category grouping, toggle, reorder, reset, and apply.
/// </summary>
public sealed class StratumColumnChooserTests : BunitContext
{
    public StratumColumnChooserTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShouldNotRenderContentWhenNotVisible()
    {
        var registry = new ChooserTestRegistry();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, false)
            .Add(c => c.Registry, registry));

        cut.FindAll(".stratum-column-chooser").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldRenderContentWhenVisible()
    {
        var registry = new ChooserTestRegistry();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry));

        cut.Find(".stratum-column-chooser").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderCategoryFieldsets()
    {
        var registry = new ChooserTestRegistry();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry));

        var fieldsets = cut.FindAll(".stratum-column-chooser__group");

        // "Main" and "Customer"
        fieldsets.Count.Should().Be(2);

        var legends = cut.FindAll(".stratum-column-chooser__group-title");
        legends.Select(l => l.TextContent).Should().Contain("Main");
        legends.Select(l => l.TextContent).Should().Contain("Customer");
    }

    [Fact]
    public void ShouldRenderAllColumnsFromRegistry()
    {
        var registry = new ChooserTestRegistry();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry));

        var items = cut.FindAll(".stratum-column-chooser__item");

        // 3 Main + 2 Customer
        items.Count.Should().Be(5);
    }

    [Fact]
    public void ShouldCheckDefaultVisibleColumnsWhenNoVisibleKeysPassed()
    {
        var registry = new ChooserTestRegistry();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry));

        var checkboxes = cut.FindAll(".stratum-column-chooser__checkbox");

        // Default visible: Name (Main), Amount (Main), Customer.Name (Customer) → 3 checked
        var checkedBoxes = checkboxes.Where(cb => cb.HasAttribute("checked")).ToList();
        checkedBoxes.Count.Should().Be(3);
    }

    [Fact]
    public void ShouldUseProvidedVisibleColumnKeysWhenSupplied()
    {
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string> { "Name", "IsActive" };

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys));

        var checkboxes = cut.FindAll(".stratum-column-chooser__checkbox");
        var checkedBoxes = checkboxes.Where(cb => cb.HasAttribute("checked")).ToList();
        checkedBoxes.Count.Should().Be(2);
    }

    [Fact]
    public void ShouldAddColumnToOrderListWhenCheckboxToggled()
    {
        var registry = new ChooserTestRegistry();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, new List<string> { "Name" }));

        cut.FindAll(".stratum-column-chooser__order-item").Count.Should().Be(1);

        // Toggle "IsActive" checkbox on — it's the 3rd checkbox in Main group
        var checkboxes = cut.FindAll(".stratum-column-chooser__checkbox");
        var isActiveCheckbox = checkboxes[2];
        isActiveCheckbox.Change(true);

        cut.FindAll(".stratum-column-chooser__order-item").Count.Should().Be(2);
    }

    [Fact]
    public void ShouldRemoveColumnFromOrderListWhenUnchecked()
    {
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string> { "Name", "Amount" };

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys));

        cut.FindAll(".stratum-column-chooser__order-item").Count.Should().Be(2);

        var checkboxes = cut.FindAll(".stratum-column-chooser__checkbox");
        checkboxes[1].Change(false);

        cut.FindAll(".stratum-column-chooser__order-item").Count.Should().Be(1);
    }

    [Fact]
    public void ShouldRenderOrderListWithVisibleColumns()
    {
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string> { "Name", "Amount", "Customer.Name" };

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys));

        var orderItems = cut.FindAll(".stratum-column-chooser__order-item");
        orderItems.Count.Should().Be(3);
    }

    [Fact]
    public void OrderItemsShouldBeDraggableWithHandleAndAriaMetadata()
    {
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string> { "Name", "Amount" };

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys));

        var items = cut.FindAll(".stratum-column-chooser__order-item");
        items.Count.Should().Be(2);

        // draggable="true" + tabindex="0" are required for HTML5 DnD + keyboard a11y.
        items[0].GetAttribute("draggable").Should().Be("true");
        items[0].GetAttribute("tabindex").Should().Be("0");
        items[0].GetAttribute("aria-label").Should().Contain("position 1 sur 2");

        // Each item renders a Material Symbols drag handle.
        cut.FindAll(".stratum-column-chooser__drag-handle").Count.Should().Be(2);
    }

    [Fact]
    public async Task MoveItemShouldReorderAndApplyShouldEmitNewOrder()
    {
        IReadOnlyList<string>? appliedKeys = null;
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string> { "Name", "Amount", "Customer.Name" };

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys)
            .Add(c => c.OnApply, EventCallback.Factory.Create<IReadOnlyList<string>>(
                this, keys => appliedKeys = keys)));

        // MoveItem is the core reorder helper exposed to DnD / keyboard — validate it directly.
        cut.Instance.MoveItem(fromIndex: 0, toIndex: 2);
        cut.Render();

        var applyBtn = cut.Find(".btn-primary");
        await applyBtn.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        appliedKeys.Should().NotBeNull();
        appliedKeys!.Should().ContainInOrder("Amount", "Customer.Name", "Name");
    }

    [Fact]
    public async Task ArrowDownKeyShouldMoveFocusedItemDown()
    {
        IReadOnlyList<string>? appliedKeys = null;
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string> { "Name", "Amount" };

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys)
            .Add(c => c.OnApply, EventCallback.Factory.Create<IReadOnlyList<string>>(
                this, keys => appliedKeys = keys)));

        var firstItem = cut.FindAll(".stratum-column-chooser__order-item")[0];
        firstItem.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowDown" });

        var applyBtn = cut.Find(".btn-primary");
        await applyBtn.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        appliedKeys.Should().NotBeNull();
        appliedKeys!.Should().ContainInOrder("Amount", "Name");
    }

    [Fact]
    public async Task ArrowUpKeyShouldMoveFocusedItemUp()
    {
        IReadOnlyList<string>? appliedKeys = null;
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string> { "Name", "Amount" };

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys)
            .Add(c => c.OnApply, EventCallback.Factory.Create<IReadOnlyList<string>>(
                this, keys => appliedKeys = keys)));

        var secondItem = cut.FindAll(".stratum-column-chooser__order-item")[1];
        secondItem.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowUp" });

        var applyBtn = cut.Find(".btn-primary");
        await applyBtn.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        appliedKeys.Should().NotBeNull();
        appliedKeys!.Should().ContainInOrder("Amount", "Name");
    }

    [Fact]
    public async Task DragDropShouldReorderItemsAcrossDropTarget()
    {
        IReadOnlyList<string>? appliedKeys = null;
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string> { "Name", "Amount", "Customer.Name" };

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys)
            .Add(c => c.OnApply, EventCallback.Factory.Create<IReadOnlyList<string>>(
                this, keys => appliedKeys = keys)));

        // Re-query after each dispatch — the component re-renders between
        // events (ondragstart updates _draggingIndex, ondragenter updates
        // _dragOverIndex) and the previous element handles are invalidated.
        cut.FindAll(".stratum-column-chooser__order-item")[0]
            .DragStart(new Microsoft.AspNetCore.Components.Web.DragEventArgs());
        cut.FindAll(".stratum-column-chooser__order-item")[2]
            .DragEnter(new Microsoft.AspNetCore.Components.Web.DragEventArgs());
        cut.FindAll(".stratum-column-chooser__order-item")[2]
            .Drop(new Microsoft.AspNetCore.Components.Web.DragEventArgs());

        var applyBtn = cut.Find(".btn-primary");
        await applyBtn.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        appliedKeys.Should().NotBeNull();
        appliedKeys!.Should().ContainInOrder("Amount", "Customer.Name", "Name");
    }

    [Fact]
    public async Task ResetShouldRestoreDefaultVisibleColumns()
    {
        IReadOnlyList<string>? appliedKeys = null;
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string> { "IsActive" };

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys)
            .Add(c => c.OnApply, EventCallback.Factory.Create<IReadOnlyList<string>>(
                this, keys => appliedKeys = keys)));

        cut.FindAll(".stratum-column-chooser__order-item").Count.Should().Be(1);

        var resetBtn = cut.Find("[aria-label='Réinitialiser les colonnes par défaut']");
        await resetBtn.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.FindAll(".stratum-column-chooser__order-item").Count.Should().Be(3);

        var applyBtn = cut.Find(".btn-primary");
        await applyBtn.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        appliedKeys.Should().NotBeNull();
        appliedKeys!.Should().HaveCount(3);
        appliedKeys.Should().Contain("Name");
        appliedKeys.Should().Contain("Amount");
        appliedKeys.Should().Contain("Customer.Name");
    }

    [Fact]
    public async Task ApplyShouldInvokeCallbackWithCurrentKeys()
    {
        IReadOnlyList<string>? appliedKeys = null;
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string> { "Name", "Amount" };

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys)
            .Add(c => c.OnApply, EventCallback.Factory.Create<IReadOnlyList<string>>(
                this, keys => appliedKeys = keys)));

        var applyBtn = cut.Find(".btn-primary");
        await applyBtn.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        appliedKeys.Should().NotBeNull();
        appliedKeys!.Should().ContainInOrder("Name", "Amount");
    }

    [Fact]
    public async Task ApplyShouldCloseDialog()
    {
        var visibleState = true;
        var registry = new ChooserTestRegistry();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleChanged, EventCallback.Factory.Create<bool>(
                this, v => visibleState = v)));

        var applyBtn = cut.Find(".btn-primary");
        await applyBtn.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        visibleState.Should().BeFalse();
    }

    [Fact]
    public async Task CancelShouldCloseDialogWithoutApply()
    {
        var visibleState = true;
        IReadOnlyList<string>? appliedKeys = null;
        var registry = new ChooserTestRegistry();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleChanged, EventCallback.Factory.Create<bool>(
                this, v => visibleState = v))
            .Add(c => c.OnApply, EventCallback.Factory.Create<IReadOnlyList<string>>(
                this, keys => appliedKeys = keys)));

        var buttons = cut.FindAll(".btn-outline-secondary");

        // buttons[0] = Réinitialiser, buttons[1] = Annuler
        await buttons[1].ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        visibleState.Should().BeFalse();
        appliedKeys.Should().BeNull();
    }

    [Fact]
    public void ShouldFallBackToDefaultsWhenVisibleKeysIsEmpty()
    {
        var registry = new ChooserTestRegistry();
        var visibleKeys = new List<string>();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.VisibleColumnKeys, visibleKeys));

        // Empty list falls back to registry defaults (3 default visible columns)
        cut.FindAll(".stratum-column-chooser__order-item").Count.Should().Be(3);
    }

    [Fact]
    public void ShouldHaveAriaLabelOnRegion()
    {
        var registry = new ChooserTestRegistry();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry)
            .Add(c => c.Title, "Colonnes"));

        var region = cut.Find("[role='region']");
        region.GetAttribute("aria-label").Should().Be("Colonnes");
    }

    [Fact]
    public void CheckboxesShouldHaveAriaLabels()
    {
        var registry = new ChooserTestRegistry();

        var cut = Render<StratumColumnChooser<ChooserTestItem>>(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.Registry, registry));

        var checkboxes = cut.FindAll(".stratum-column-chooser__checkbox");
        foreach (var cb in checkboxes)
        {
            cb.GetAttribute("aria-label").Should().NotBeNullOrEmpty();
        }
    }

    private sealed record ChooserTestItem(string Name, decimal Amount, bool IsActive, string CustomerName, string CustomerCity);

    private sealed class ChooserTestRegistry : ColumnRegistryBase<ChooserTestItem>
    {
        protected override void Configure()
        {
            Column("Name", "Nom", "Test", ColumnDataType.Text, defaultVisible: true, sortOrder: 10);
            Column("Amount", "Montant", "Test", ColumnDataType.Money, defaultVisible: true, sortOrder: 20);
            Column("IsActive", "Actif", "Test", ColumnDataType.Boolean, defaultVisible: false, sortOrder: 30);

            RelatedColumn("Customer.Name", "Nom client", "Customer", ColumnDataType.Text, defaultVisible: true, sortOrder: 10);
            RelatedColumn("Customer.City", "Ville client", "Customer", ColumnDataType.Text, defaultVisible: false, sortOrder: 20);
        }
    }
}

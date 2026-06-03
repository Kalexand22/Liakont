namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class StratumRepeatingSectionTests : BunitContext
{
    public StratumRepeatingSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShouldRenderEmptyStateWhenNoItems()
    {
        var items = new List<TestItem>();

        var cut = RenderSection(items);

        var empty = cut.Find("[data-testid='test-section-empty']");
        empty.TextContent.Should().Contain("Aucun");
    }

    [Fact]
    public void ShouldRenderRowsWhenItemsProvided()
    {
        var items = new List<TestItem>
        {
            new() { Name = "Alpha", Value = "A" },
            new() { Name = "Beta", Value = "B" },
        };

        var cut = RenderSection(items);

        var rows = cut.FindAll("[data-testid^='test-section-row-']");
        rows.Count.Should().Be(2);
    }

    [Fact]
    public void ShouldAddItemWhenAddButtonClicked()
    {
        var items = new List<TestItem>();
        var changeCount = 0;

        var cut = RenderSection(items, onChanged: _ => changeCount++);

        var addBtn = cut.Find("[data-testid='test-section-add-btn']");
        addBtn.Click();

        items.Count.Should().Be(1);
        changeCount.Should().Be(1);
    }

    [Fact]
    public void ShouldRemoveItemWhenRemoveButtonClicked()
    {
        var items = new List<TestItem>
        {
            new() { Name = "ToRemove", Value = "X" },
        };
        var changeCount = 0;

        var cut = RenderSection(items, onChanged: _ => changeCount++);

        var removeBtn = cut.Find("[data-testid='test-section-remove-btn-0']");
        removeBtn.Click();

        items.Count.Should().Be(0);
        changeCount.Should().Be(1);
    }

    [Fact]
    public void ShouldHideAddAndRemoveButtonsWhenDisabled()
    {
        var items = new List<TestItem>
        {
            new() { Name = "Item", Value = "V" },
        };

        var cut = RenderSection(items, disabled: true);

        cut.FindAll("[data-testid='test-section-add-btn']").Count.Should().Be(0);
        cut.FindAll("[data-testid^='test-section-remove-btn-']").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldShowAddButtonWhenNotDisabled()
    {
        var items = new List<TestItem>();

        var cut = RenderSection(items);

        cut.FindAll("[data-testid='test-section-add-btn']").Count.Should().Be(1);
    }

    [Fact]
    public void ShouldRenderCustomEmptyText()
    {
        var items = new List<TestItem>();

        var cut = Render<StratumRepeatingSection<TestItem>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.NewItemFactory, () => new TestItem())
            .Add(p => p.Title, "Custom")
            .Add(p => p.EmptyText, "Nothing here")
            .Add(p => p.TestId, "custom-section")
            .Add(p => p.HeaderTemplate, BuildHeaderFragment())
            .Add(p => p.RowTemplate, BuildRowFragment()));

        var empty = cut.Find("[data-testid='custom-section-empty']");
        empty.TextContent.Should().Contain("Nothing here");
    }

    [Fact]
    public void ShouldRenderHeaderColumns()
    {
        var items = new List<TestItem>
        {
            new() { Name = "Test", Value = "V" },
        };

        var cut = RenderSection(items);

        var headers = cut.FindAll("thead th");
        headers.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void ShouldHideAddButtonWhenShowAddButtonFalse()
    {
        var items = new List<TestItem>
        {
            new() { Name = "Item", Value = "V" },
        };

        var cut = Render<StratumRepeatingSection<TestItem>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.NewItemFactory, () => new TestItem())
            .Add(p => p.Title, "Test Items")
            .Add(p => p.ShowAddButton, false)
            .Add(p => p.TestId, "test-section")
            .Add(p => p.HeaderTemplate, BuildHeaderFragment())
            .Add(p => p.RowTemplate, BuildRowFragment()));

        cut.FindAll("[data-testid='test-section-add-btn']").Count.Should().Be(0, "Add button must be hidden when ShowAddButton=false");
        cut.FindAll("[data-testid^='test-section-remove-btn-']").Count.Should().Be(1, "Remove buttons remain visible when ShowAddButton=false");
    }

    [Fact]
    public void ShouldRenderTestId()
    {
        var items = new List<TestItem>();

        var cut = RenderSection(items);

        cut.Find("[data-testid='test-section']").Should().NotBeNull();
    }

#pragma warning disable ASP0006

    private static RenderFragment BuildHeaderFragment()
    {
        return builder =>
        {
            builder.OpenElement(0, "th");
            builder.AddContent(1, "Name");
            builder.CloseElement();
            builder.OpenElement(2, "th");
            builder.AddContent(3, "Value");
            builder.CloseElement();
        };
    }

    private static RenderFragment<TestItem> BuildRowFragment()
    {
        return item => builder =>
        {
            builder.OpenElement(0, "td");
            builder.AddContent(1, item.Name);
            builder.CloseElement();
            builder.OpenElement(2, "td");
            builder.AddContent(3, item.Value);
            builder.CloseElement();
        };
    }

#pragma warning restore ASP0006

    private IRenderedComponent<StratumRepeatingSection<TestItem>> RenderSection(
        List<TestItem> items,
        bool disabled = false,
        Action<IList<TestItem>>? onChanged = null)
    {
        return Render<StratumRepeatingSection<TestItem>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.ItemsChanged, EventCallback.Factory.Create<IList<TestItem>>(this, onChanged ?? (_ => { })))
            .Add(p => p.NewItemFactory, () => new TestItem())
            .Add(p => p.Title, "Test Items")
            .Add(p => p.Disabled, disabled)
            .Add(p => p.TestId, "test-section")
            .Add(p => p.HeaderTemplate, BuildHeaderFragment())
            .Add(p => p.RowTemplate, BuildRowFragment()));
    }

    private sealed class TestItem
    {
        public string Name { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
    }
}

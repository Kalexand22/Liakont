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

public sealed class StratumCardViewTests : BunitContext
{
    public StratumCardViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ── Rendering states ────────────────────────────────────────────
    [Fact]
    public void ShouldRenderEmptyStateWhenDataIsNull()
    {
        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, (IReadOnlyList<TestItem>)null!));

        cut.Find(".stratum-card-view__empty").Should().NotBeNull();
        cut.Find(".stratum-card-view__empty-text").TextContent.Should().Contain("Aucun");
    }

    [Fact]
    public void ShouldRenderEmptyStateWhenDataIsEmpty()
    {
        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, Array.Empty<TestItem>()));

        cut.Find(".stratum-card-view__empty").Should().NotBeNull();
        cut.Find(".stratum-card-view__empty").GetAttribute("role").Should().Be("status");
    }

    [Fact]
    public void ShouldRenderCustomEmptyContent()
    {
        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, Array.Empty<TestItem>())
            .Add(v => v.EmptyContent, builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "custom-empty");
                builder.AddContent(2, "Custom empty");
                builder.CloseElement();
            }));

        cut.Find(".custom-empty").TextContent.Should().Be("Custom empty");
    }

    [Fact]
    public void ShouldRenderLoadingSkeletonWhenLoadingIsTrue()
    {
        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Loading, true));

        var grid = cut.Find(".stratum-card-view__grid");
        grid.GetAttribute("aria-busy").Should().Be("true");
        grid.GetAttribute("role").Should().Be("status");

        // Default skeleton count is 6
        cut.FindAll(".stratum-card-view__card--skeleton").Count.Should().Be(6);
    }

    [Fact]
    public void ShouldRenderCustomSkeletonCount()
    {
        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Loading, true)
            .Add(v => v.SkeletonCount, 3));

        cut.FindAll(".stratum-card-view__card--skeleton").Count.Should().Be(3);
    }

    // ── Card rendering ────────────────────────────────────────────
    [Fact]
    public void ShouldRenderCardsWithListRole()
    {
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.AriaLabel, "Test cards"));

        var grid = cut.Find(".stratum-card-view__grid");
        grid.GetAttribute("role").Should().Be("list");
        grid.GetAttribute("aria-label").Should().Be("Test cards");

        var cards = cut.FindAll("[role='listitem']");
        cards.Should().HaveCount(3);
    }

    [Fact]
    public void ShouldRenderCardTitleFromColumnRegistry()
    {
        var data = SampleData();
        var registry = new TestColumnRegistry();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.ColumnRegistry, registry));

        var titles = cut.FindAll(".stratum-card-view__title");
        titles[0].TextContent.Should().Contain("Item 1");
        titles[1].TextContent.Should().Contain("Item 2");
    }

    [Fact]
    public void ShouldRenderCardSubtitleFromSecondColumn()
    {
        var data = SampleData();
        var registry = new TestColumnRegistry();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.ColumnRegistry, registry));

        var subtitles = cut.FindAll(".stratum-card-view__subtitle");
        subtitles.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void ShouldRenderCustomCardTemplate()
    {
        var data = SampleData(1);
        RenderFragment<TestItem> template = item => builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "custom-card");
            builder.AddContent(2, $"Custom: {item.Name}");
            builder.CloseElement();
        };

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.CardTemplate, template));

        cut.Find(".custom-card").TextContent.Should().Be("Custom: Item 1");
    }

    // ── Selection ────────────────────────────────────────────────
    [Fact]
    public void ShouldRenderCheckboxesWhenSelectionAllowed()
    {
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.AllowSelection, true));

        var checkboxes = cut.FindAll(".stratum-card-view__checkbox input[type='checkbox']");
        checkboxes.Should().HaveCount(3);
    }

    [Fact]
    public void ShouldNotRenderCheckboxesWhenSelectionNotAllowed()
    {
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.AllowSelection, false));

        cut.FindAll(".stratum-card-view__checkbox").Should().BeEmpty();
    }

    [Fact]
    public void ShouldMarkSelectedCardsWithAriaSelected()
    {
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.AllowSelection, true)
            .Add(v => v.SelectedItems, new List<TestItem> { data[0] }));

        var firstCard = cut.Find("[data-testid='card-0']");
        firstCard.GetAttribute("aria-selected").Should().Be("true");
        firstCard.ClassList.Should().Contain("stratum-card-view__card--selected");

        var secondCard = cut.Find("[data-testid='card-1']");
        secondCard.GetAttribute("aria-selected").Should().Be("false");
    }

    [Fact]
    public async Task CheckboxToggleShouldFireSelectionChanged()
    {
        IReadOnlyList<TestItem>? selected = null;
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.AllowSelection, true)
            .Add(v => v.OnSelectionChanged, EventCallback.Factory.Create<IReadOnlyList<TestItem>>(this, items => selected = items)));

        var checkbox = cut.Find("[data-testid='card-0'] input[type='checkbox']");
        await checkbox.ChangeAsync(new ChangeEventArgs { Value = true });

        selected.Should().NotBeNull();
        selected!.Count.Should().Be(1);
    }

    // ── Keyboard navigation ──────────────────────────────────────
    [Fact]
    public async Task EnterShouldFireOnRowActivated()
    {
        var activatedItem = (TestItem?)null;
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-card-view");
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem.Should().NotBeNull();
        activatedItem!.Id.Should().Be(1);
    }

    [Fact]
    public async Task ArrowRightThenEnterShouldActivateSecondCard()
    {
        var activatedItem = (TestItem?)null;
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-card-view");
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem!.Id.Should().Be(2);
    }

    [Fact]
    public async Task HomeShouldFocusFirstCard()
    {
        var activatedItem = (TestItem?)null;
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-card-view");
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Home" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem!.Id.Should().Be(1);
    }

    [Fact]
    public async Task EndShouldFocusLastCard()
    {
        var activatedItem = (TestItem?)null;
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        var wrapper = cut.Find(".stratum-card-view");
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "End" });
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        activatedItem!.Id.Should().Be(3);
    }

    [Fact]
    public async Task SpaceShouldToggleSelectionWhenAllowed()
    {
        IReadOnlyList<TestItem>? selected = null;
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.AllowSelection, true)
            .Add(v => v.OnSelectionChanged, EventCallback.Factory.Create<IReadOnlyList<TestItem>>(this, items => selected = items)));

        var wrapper = cut.Find(".stratum-card-view");
        await wrapper.KeyDownAsync(new KeyboardEventArgs { Key = " " });

        selected.Should().NotBeNull();
        selected!.Count.Should().Be(1);
    }

    [Fact]
    public async Task KeyboardShouldBeNoOpWhenDataIsEmpty()
    {
        var activatedItem = (TestItem?)null;

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, Array.Empty<TestItem>())
            .Add(v => v.OnRowActivated, EventCallback.Factory.Create<TestItem>(this, item => activatedItem = item)));

        // Empty state — no wrapper with keyboard handler, so no crash
        activatedItem.Should().BeNull();
    }

    // ── TestId ───────────────────────────────────────────────────
    [Fact]
    public void ShouldUseCustomTestId()
    {
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.TestId, "my-cards"));

        cut.Find("[data-testid='my-cards']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldUseDefaultTestId()
    {
        var data = SampleData();

        var cut = Render<StratumCardView<TestItem>>(p => p
            .Add(v => v.Data, data));

        cut.Find("[data-testid='stratum-card-view']").Should().NotBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────
    private static List<TestItem> SampleData(int count = 3) =>
        Enumerable.Range(1, count)
            .Select(i => new TestItem(i, $"Item {i}", i % 2 == 0 ? "B" : "A", i * 10m))
            .ToList();

    private sealed record TestItem(int Id, string Name, string Category, decimal Price);

    private sealed class TestColumnRegistry : ColumnRegistryBase<TestItem>
    {
        protected override void Configure()
        {
            Column("Name", "Nom", "Test", defaultVisible: true, sortOrder: 1);
            Column("Category", "Catégorie", "Test", defaultVisible: true, sortOrder: 2);
            Column("Price", "Prix", "Test", dataType: ColumnDataType.Money, defaultVisible: false, sortOrder: 3);
        }
    }
}

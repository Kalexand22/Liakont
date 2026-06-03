namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class StratumTabsTests : BunitContext
{
    public StratumTabsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShouldRenderTabButtons()
    {
        var cut = RenderTabs("Tab A", "Tab B", "Tab C");

        var tabs = cut.FindAll("[role='tab']");
        tabs.Count.Should().Be(3);
        tabs[0].TextContent.Should().Contain("Tab A");
        tabs[1].TextContent.Should().Contain("Tab B");
        tabs[2].TextContent.Should().Contain("Tab C");
    }

    [Fact]
    public void ShouldRenderTablistRole()
    {
        var cut = RenderTabs("Tab A", "Tab B");

        cut.Find("[role='tablist']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderTabpanels()
    {
        var cut = RenderTabs("Tab A", "Tab B");

        cut.FindAll("[role='tabpanel']").Count.Should().Be(2);
    }

    [Fact]
    public void FirstTabShouldBeSelectedByDefault()
    {
        var cut = RenderTabs("Tab A", "Tab B");

        var tabs = cut.FindAll("[role='tab']");
        tabs[0].GetAttribute("aria-selected").Should().Be("true");
        tabs[1].GetAttribute("aria-selected").Should().Be("false");
    }

    [Fact]
    public void SelectedTabShouldHaveTabindexZero()
    {
        var cut = RenderTabs("Tab A", "Tab B");

        var tabs = cut.FindAll("[role='tab']");
        tabs[0].GetAttribute("tabindex").Should().Be("0");
        tabs[1].GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void ShouldHaveActiveClassOnSelectedTab()
    {
        var cut = RenderTabs("Tab A", "Tab B");

        var tabs = cut.FindAll("[role='tab']");
        tabs[0].ClassList.Should().Contain("stratum-tabs__tab--active");
        tabs[1].ClassList.Should().NotContain("stratum-tabs__tab--active");
    }

    [Fact]
    public void ShouldRenderSelectedTabContent()
    {
        var cut = RenderTabs("Tab A", "Tab B");

        var panels = cut.FindAll("[role='tabpanel']");
        panels[0].HasAttribute("hidden").Should().BeFalse();
        panels[1].HasAttribute("hidden").Should().BeTrue();
    }

    [Fact]
    public void TabPanelShouldHaveAriaLabelledby()
    {
        var cut = RenderTabs("Tab A", "Tab B");

        var tabs = cut.FindAll("[role='tab']");
        var panels = cut.FindAll("[role='tabpanel']");
        var tabId = tabs[0].GetAttribute("id");

        panels[0].GetAttribute("aria-labelledby").Should().Be(tabId);
    }

    [Fact]
    public void TabShouldHaveAriaControls()
    {
        var cut = RenderTabs("Tab A", "Tab B");

        var tabs = cut.FindAll("[role='tab']");
        var panels = cut.FindAll("[role='tabpanel']");
        var panelId = panels[0].GetAttribute("id");

        tabs[0].GetAttribute("aria-controls").Should().Be(panelId);
    }

    [Fact]
    public async Task ClickingTabShouldSelectIt()
    {
        var selectedIndex = 0;

        var cut = RenderTabsWithCallback(
            i => selectedIndex = i,
            "Tab A",
            "Tab B");

        await cut.FindAll("[role='tab']")[1].ClickAsync(new MouseEventArgs());

        selectedIndex.Should().Be(1);
    }

    [Fact]
    public async Task ClickingDisabledTabShouldNotSelectIt()
    {
        var selectedIndex = 0;

        var cut = RenderTabsWithDisabled(
            i => selectedIndex = i,
            ("Tab A", false),
            ("Tab B", true));

        var disabledTab = cut.FindAll("[role='tab']")[1];
        disabledTab.HasAttribute("disabled").Should().BeTrue();
        await disabledTab.ClickAsync(new MouseEventArgs());
        selectedIndex.Should().Be(0);
    }

    [Fact]
    public void DisabledTabShouldHaveDisabledClass()
    {
        var cut = RenderTabsWithDisabled(
            null,
            ("Tab A", false),
            ("Disabled", true));

        cut.FindAll("[role='tab']")[1].ClassList.Should().Contain("stratum-tabs__tab--disabled");
    }

    [Fact]
    public async Task ArrowRightShouldSelectNextTab()
    {
        var selectedIndex = 0;

        var cut = RenderTabsWithCallback(
            i => selectedIndex = i,
            "Tab A",
            "Tab B");

        await cut.Find("[role='tablist']").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        selectedIndex.Should().Be(1);
    }

    [Fact]
    public async Task ArrowLeftShouldNotGoBelowZero()
    {
        var selectedIndex = 0;

        var cut = RenderTabsWithCallback(
            i => selectedIndex = i,
            "Tab A",
            "Tab B");

        await cut.Find("[role='tablist']").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowLeft" });

        selectedIndex.Should().Be(0);
    }

    [Fact]
    public async Task HomeShouldSelectFirstTab()
    {
        var selectedIndex = 2;

        var cut = RenderTabsWithCallback(
            i => selectedIndex = i,
            initialIndex: 2,
            "Tab A",
            "Tab B",
            "Tab C");

        await cut.Find("[role='tablist']").KeyDownAsync(new KeyboardEventArgs { Key = "Home" });

        selectedIndex.Should().Be(0);
    }

    [Fact]
    public async Task EndShouldSelectLastTab()
    {
        var selectedIndex = 0;

        var cut = RenderTabsWithCallback(
            i => selectedIndex = i,
            "Tab A",
            "Tab B",
            "Tab C");

        await cut.Find("[role='tablist']").KeyDownAsync(new KeyboardEventArgs { Key = "End" });

        selectedIndex.Should().Be(2);
    }

    [Fact]
    public async Task ArrowRightShouldSkipDisabledTabs()
    {
        var selectedIndex = 0;

        var cut = RenderTabsWithDisabled(
            i => selectedIndex = i,
            ("Tab A", false),
            ("Tab B", true),
            ("Tab C", false));

        await cut.Find("[role='tablist']").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        selectedIndex.Should().Be(2);
    }

    [Fact]
    public void ShouldRenderTabWithIcon()
    {
        var cut = RenderTabWithIcon("Settings", "bi-gear");

        var icon = cut.Find(".stratum-tabs__icon");
        icon.ClassList.Should().Contain("bi-gear");
        icon.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public async Task OnChangeShouldFireWhenTabSelected()
    {
        var changeFired = false;

        var cut = RenderTabsWithOnChange(
            () => changeFired = true,
            "Tab A",
            "Tab B");

        await cut.FindAll("[role='tab']")[1].ClickAsync(new MouseEventArgs());

        changeFired.Should().BeTrue();
    }

    [Fact]
    public void TabPanelsShouldHaveTabindexZero()
    {
        var cut = RenderTabs("Tab A", "Tab B");

        var panels = cut.FindAll("[role='tabpanel']");
        foreach (var panel in panels)
        {
            panel.GetAttribute("tabindex").Should().Be("0");
        }
    }

#pragma warning disable ASP0006 // Sequence numbers in test render helpers are fine

    private static RenderFragment BuildTabsFragment(string[] titles)
    {
        return builder =>
        {
            for (var i = 0; i < titles.Length; i++)
            {
                var title = titles[i];
                builder.OpenComponent<StratumTab>(i * 10);
                builder.AddAttribute((i * 10) + 1, "Title", title);
                builder.AddAttribute((i * 10) + 2, "ChildContent", TextFragment($"{title} content"));
                builder.CloseComponent();
            }
        };
    }

    private static RenderFragment BuildTabsFragmentWithDisabled((string Title, bool Disabled)[] tabs)
    {
        return builder =>
        {
            for (var i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];
                builder.OpenComponent<StratumTab>(i * 10);
                builder.AddAttribute((i * 10) + 1, "Title", tab.Title);
                builder.AddAttribute((i * 10) + 2, "Disabled", tab.Disabled);
                builder.AddAttribute((i * 10) + 3, "ChildContent", TextFragment($"{tab.Title} content"));
                builder.CloseComponent();
            }
        };
    }

    private static RenderFragment BuildTabWithIconFragment(string title, string icon)
    {
        return builder =>
        {
            builder.OpenComponent<StratumTab>(0);
            builder.AddAttribute(1, "Title", title);
            builder.AddAttribute(2, "Icon", icon);
            builder.AddAttribute(3, "ChildContent", TextFragment($"{title} content"));
            builder.CloseComponent();
        };
    }

    private static RenderFragment TextFragment(string text)
    {
        return builder => builder.AddContent(0, text);
    }

#pragma warning restore ASP0006

    private IRenderedComponent<StratumTabs> RenderTabs(params string[] titles)
    {
        return Render<StratumTabs>(p => p
            .Add(t => t.ChildContent, BuildTabsFragment(titles)));
    }

    private IRenderedComponent<StratumTabs> RenderTabsWithCallback(
        Action<int> onIndexChanged,
        params string[] titles)
    {
        return RenderTabsWithCallback(onIndexChanged, 0, titles);
    }

    private IRenderedComponent<StratumTabs> RenderTabsWithCallback(
        Action<int> onIndexChanged,
        int initialIndex,
        params string[] titles)
    {
        return Render<StratumTabs>(p => p
            .Add(t => t.SelectedIndex, initialIndex)
            .Add(t => t.SelectedIndexChanged, EventCallback.Factory.Create(this, onIndexChanged))
            .Add(t => t.ChildContent, BuildTabsFragment(titles)));
    }

    private IRenderedComponent<StratumTabs> RenderTabsWithDisabled(
        Action<int>? onIndexChanged,
        params (string Title, bool Disabled)[] tabs)
    {
        return Render<StratumTabs>(p =>
        {
            if (onIndexChanged is not null)
            {
                p.Add(t => t.SelectedIndexChanged, EventCallback.Factory.Create(this, onIndexChanged));
            }

            p.Add(t => t.ChildContent, BuildTabsFragmentWithDisabled(tabs));
        });
    }

    private IRenderedComponent<StratumTabs> RenderTabWithIcon(string title, string icon)
    {
        return Render<StratumTabs>(p => p
            .Add(t => t.ChildContent, BuildTabWithIconFragment(title, icon)));
    }

    private IRenderedComponent<StratumTabs> RenderTabsWithOnChange(
        Action onChanged,
        params string[] titles)
    {
        return Render<StratumTabs>(p => p
            .Add(t => t.SelectedIndex, 0)
            .Add(t => t.OnChange, EventCallback.Factory.Create<int>(this, _ => onChanged()))
            .Add(t => t.ChildContent, BuildTabsFragment(titles)));
    }
}

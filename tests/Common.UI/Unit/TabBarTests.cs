namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class TabBarTests : BunitContext
{
    private readonly TabManagerService _tabManager = new();
    private readonly ToastService _toastService = new();
    private readonly DefaultTabTitleProvider _titleProvider = new();

    public TabBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITabManagerService>(_tabManager);
        Services.AddSingleton<IToastService>(_toastService);
        Services.AddSingleton<ITabTitleProvider>(_titleProvider);
        Services.AddSingleton<IStringLocalizer<SharedResources>>(
            new StubStringLocalizer<SharedResources>());
    }

    [Fact]
    public void ShouldRenderTablistRole()
    {
        _tabManager.OpenTab("/clients", "Clients");
        var cut = Render<TabBar>();

        cut.Find("[role='tablist']").Should().NotBeNull();
        cut.Find("[role='tablist']").GetAttribute("aria-label").Should().Be("Tab_AriaLabel");
    }

    [Fact]
    public void ShouldRenderTabsFromService()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");
        var cut = Render<TabBar>();

        var tabs = cut.FindAll("[role='tab']");
        tabs.Count.Should().Be(2);
        tabs[0].TextContent.Should().Contain("Clients");
        tabs[1].TextContent.Should().Contain("Quotes");
    }

    [Fact]
    public void ActiveTabShouldHaveAriaSelectedTrue()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");
        var cut = Render<TabBar>();

        // Last opened tab is active
        var tabs = cut.FindAll("[role='tab']");
        tabs[0].GetAttribute("aria-selected").Should().Be("false");
        tabs[1].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void ActiveTabShouldHaveTabindexZero()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");
        var cut = Render<TabBar>();

        var tabs = cut.FindAll("[role='tab']");
        tabs[0].GetAttribute("tabindex").Should().Be("-1");
        tabs[1].GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void ActiveTabShouldHaveActiveCssClass()
    {
        _tabManager.OpenTab("/clients", "Clients");
        var cut = Render<TabBar>();

        var tab = cut.Find("[role='tab']");
        tab.ClassList.Should().Contain("tab-bar__tab--active");
    }

    [Fact]
    public void ShouldRenderCloseButtonForUnpinnedTabs()
    {
        _tabManager.OpenTab("/clients", "Clients");
        var cut = Render<TabBar>();

        var closeBtn = cut.Find("[data-testid='tab-bar-close-0']");
        closeBtn.Should().NotBeNull();
        closeBtn.GetAttribute("aria-label").Should().Be("Tab_CloseAriaLabel");
    }

    [Fact]
    public void ShouldNotRenderCloseButtonForPinnedTabs()
    {
        _tabManager.OpenTab("/home", "Home", pinned: true);
        var cut = Render<TabBar>();

        cut.FindAll("[data-testid='tab-bar-close-0']").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldRenderNewTabButton()
    {
        var cut = Render<TabBar>();

        var newBtn = cut.Find("[data-testid='tab-bar-new']");
        newBtn.Should().NotBeNull();
        newBtn.GetAttribute("aria-label").Should().Be("Tab_NewTabAriaLabel");
    }

    [Fact]
    public void ShouldRenderIconWhenProvided()
    {
        _tabManager.OpenTab("/clients", "Clients", icon: "bi-people");
        var cut = Render<TabBar>();

        var icon = cut.Find(".tab-bar__icon");
        icon.ClassList.Should().Contain("bi-people");
        icon.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void ShouldRenderDataTestIdOnRoot()
    {
        var cut = Render<TabBar>();

        cut.Find("[data-testid='tab-bar']").Should().NotBeNull();
    }

    [Fact]
    public void ClickTabShouldSwitchActiveTab()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");
        var cut = Render<TabBar>();

        // Click first tab (Clients)
        cut.Find("[data-testid='tab-bar-tab-0']").Click();

        _tabManager.ActiveTabId.Should().Be(_tabManager.GetTabs()[0].Id);
    }

    [Fact]
    public void ClickTabShouldNavigateToTabUrl()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");
        var cut = Render<TabBar>();

        cut.Find("[data-testid='tab-bar-tab-0']").Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().EndWith("/clients");
    }

    [Fact]
    public void CloseTabShouldRemoveTab()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");
        var cut = Render<TabBar>();

        cut.Find("[data-testid='tab-bar-close-1']").Click();

        _tabManager.GetTabs().Should().HaveCount(1);
        cut.FindAll("[role='tab']").Count.Should().Be(1);
    }

    [Fact]
    public void NewTabButtonShouldNavigateToHome()
    {
        var cut = Render<TabBar>();

        cut.Find("[data-testid='tab-bar-new']").Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().EndWith("/");
    }

    [Fact]
    public void ShouldUpdateWhenServiceRaisesEvent()
    {
        var cut = Render<TabBar>();

        // Initial tab created for current URL (/)
        var initialCount = cut.FindAll("[role='tab']").Count;
        initialCount.Should().Be(1);

        _tabManager.OpenTab("/clients", "Clients");
        cut.Render(); // Re-render after event

        cut.FindAll("[role='tab']").Count.Should().Be(initialCount + 1);
    }

    [Fact]
    public void ArrowRightShouldMoveToNextTab()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");
        _tabManager.SwitchTab(_tabManager.GetTabs()[0].Id); // Activate first
        var cut = Render<TabBar>();

        cut.Find("[role='tablist']").KeyDown(Key.Right);

        _tabManager.ActiveTabId.Should().Be(_tabManager.GetTabs()[1].Id);
    }

    [Fact]
    public void ArrowLeftShouldMoveToPreviousTabWithWrap()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");

        // Active is Quotes (index 1, last opened)
        var cut = Render<TabBar>();

        cut.Find("[role='tablist']").KeyDown(Key.Left);

        _tabManager.ActiveTabId.Should().Be(_tabManager.GetTabs()[0].Id);
    }

    [Fact]
    public void HomeShouldMoveToFirstTab()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");
        var cut = Render<TabBar>();

        cut.Find("[role='tablist']").KeyDown(Key.Home);

        _tabManager.ActiveTabId.Should().Be(_tabManager.GetTabs()[0].Id);
    }

    [Fact]
    public void EndShouldMoveToLastTab()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");
        _tabManager.SwitchTab(_tabManager.GetTabs()[0].Id);
        var cut = Render<TabBar>();

        cut.Find("[role='tablist']").KeyDown(Key.End);

        _tabManager.ActiveTabId.Should().Be(_tabManager.GetTabs()[1].Id);
    }

    [Theory]
    [InlineData("/", "Home")]
    [InlineData("", "Home")]
    [InlineData("/clients", "Clients")]
    [InlineData("/quotes/create", "Create")]
    [InlineData("/showcase/status-badge", "Status badge")]
    public void DefaultTitleProviderShouldReturnExpectedTitle(string url, string expected)
    {
        _titleProvider.GetTitle(url).Should().Be(expected);
    }

    [Fact]
    public void DefaultTitleProviderShouldSkipGuidSegments()
    {
        var url = "/clients/3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        _titleProvider.GetTitle(url).Should().Be("Clients");
    }

    [Fact]
    public void ShouldCreateInitialTabOnRender()
    {
        // No tabs pre-opened; TabBar should create one for current URL
        var cut = Render<TabBar>();

        _tabManager.GetTabs().Should().HaveCount(1);
        _tabManager.GetTabs()[0].Url.Should().Be("/");
        _tabManager.GetTabs()[0].Title.Should().Be("Home");
    }

    [Fact]
    public void NavigationToNewUrlShouldUpdateActiveTab()
    {
        _tabManager.OpenTab("/clients", "Clients");
        var cut = Render<TabBar>();
        var nav = Services.GetRequiredService<NavigationManager>();

        nav.NavigateTo("/quotes");

        // Navigation updates the active tab instead of creating a new one
        _tabManager.GetTabs().Should().HaveCount(1);
        _tabManager.GetTabs()[0].Url.Should().Be("/quotes");
        _tabManager.GetTabs()[0].Title.Should().Be("Quotes");
    }

    [Fact]
    public void NavigationToExistingUrlShouldUpdateActiveTab()
    {
        _tabManager.OpenTab("/clients", "Clients");
        _tabManager.OpenTab("/quotes", "Quotes");
        var cut = Render<TabBar>();
        var nav = Services.GetRequiredService<NavigationManager>();

        // Active tab is /quotes (last opened). Navigating to /clients updates it.
        nav.NavigateTo("/clients");

        _tabManager.GetTabs().Should().HaveCount(2);

        // Active tab (quotes) is now updated to /clients
        var activeTab = _tabManager.GetTabs().First(t => t.Id == _tabManager.ActiveTabId);
        activeTab.Url.Should().Be("/clients");
    }

    [Fact]
    public void NavigationShouldUpdateActiveTabTitle()
    {
        var cut = Render<TabBar>();
        var nav = Services.GetRequiredService<NavigationManager>();

        nav.NavigateTo("/showcase/status-badge");

        // Active tab updated with the new URL and inferred title
        var activeTab = _tabManager.GetTabs().FirstOrDefault(t => t.Id == _tabManager.ActiveTabId);
        activeTab.Should().NotBeNull();
        activeTab!.Url.Should().Be("/showcase/status-badge");
        activeTab.Title.Should().Be("Status badge");
    }

    [Fact]
    public void NewTabButtonShouldCreateTabWithLocalizedTitle()
    {
        _tabManager.OpenTab("/clients", "Clients");
        var cut = Render<TabBar>();
        var tabsBefore = _tabManager.GetTabs().Count;

        cut.Find("[data-testid='tab-bar-new']").Click();

        var tabs = _tabManager.GetTabs();
        tabs.Should().HaveCount(tabsBefore + 1);
        tabs[^1].Title.Should().Be("Tab_NewTab"); // Key returned by stub localizer
        tabs[^1].Url.Should().Be("/");
    }

    [Fact]
    public void NewTabButtonShouldShowToastWhenLimitReached()
    {
        // Fill up to max tabs
        for (var i = 0; i < 10; i++)
        {
            _tabManager.OpenTab($"/page-{i}", $"Page {i}");
        }

        _tabManager.GetTabs().Should().HaveCount(10);
        var cut = Render<TabBar>();

        cut.Find("[data-testid='tab-bar-new']").Click();

        // Tab count unchanged
        _tabManager.GetTabs().Should().HaveCount(10);

        // Toast shown
        _toastService.GetActiveToasts().Should().ContainSingle()
            .Which.Message.Should().Contain("Tab_LimitReached");
    }

    /// <summary>Minimal localizer that returns the key as-is (no .resx in tests).</summary>
    private sealed class StubStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}

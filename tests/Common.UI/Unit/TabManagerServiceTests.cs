namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class TabManagerServiceTests
{
    private readonly TabManagerService _sut = new();

    [Fact]
    public void OpenTabShouldAddTabAndSetActive()
    {
        _sut.OpenTab("/clients", "Clients").Should().BeTrue();

        var tabs = _sut.GetTabs();
        tabs.Should().HaveCount(1);
        tabs[0].Url.Should().Be("/clients");
        tabs[0].Title.Should().Be("Clients");
        _sut.ActiveTabId.Should().Be(tabs[0].Id);
    }

    [Fact]
    public void OpenTabShouldSetIconAndPinned()
    {
        _sut.OpenTab("/home", "Home", icon: "bi-house", pinned: true);

        var tab = _sut.GetTabs()[0];
        tab.Icon.Should().Be("bi-house");
        tab.IsPinned.Should().BeTrue();
    }

    [Fact]
    public void OpenTabShouldFireOnTabsChanged()
    {
        var fired = false;
        _sut.OnTabsChanged += () => fired = true;

        _sut.OpenTab("/x", "X");

        fired.Should().BeTrue();
    }

    [Fact]
    public void OpenTabShouldSwitchActiveToNewTab()
    {
        _sut.OpenTab("/a", "A");
        _sut.OpenTab("/b", "B");

        var tabs = _sut.GetTabs();
        _sut.ActiveTabId.Should().Be(tabs[1].Id);
    }

    [Fact]
    public void OpenTabShouldRejectWhenAtMaxLimit()
    {
        var sut = new TabManagerService { MaxTabs = 3 };

        sut.OpenTab("/1", "1").Should().BeTrue();
        sut.OpenTab("/2", "2").Should().BeTrue();
        sut.OpenTab("/3", "3").Should().BeTrue();
        sut.OpenTab("/4", "4").Should().BeFalse();

        sut.GetTabs().Should().HaveCount(3);
    }

    [Fact]
    public void DefaultMaxTabsShouldBeTen()
    {
        _sut.MaxTabs.Should().Be(10);
    }

    [Fact]
    public void CloseTabShouldRemoveTab()
    {
        _sut.OpenTab("/a", "A");
        var id = _sut.GetTabs()[0].Id;

        _sut.CloseTab(id).Should().BeTrue();

        _sut.GetTabs().Should().BeEmpty();
        _sut.ActiveTabId.Should().BeNull();
    }

    [Fact]
    public void CloseTabShouldReturnFalseForUnknownId()
    {
        _sut.CloseTab(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void CloseTabShouldNotClosePinnedTab()
    {
        _sut.OpenTab("/home", "Home", pinned: true);
        var id = _sut.GetTabs()[0].Id;

        _sut.CloseTab(id).Should().BeFalse();
        _sut.GetTabs().Should().HaveCount(1);
    }

    [Fact]
    public void CloseTabShouldFireOnTabsChanged()
    {
        _sut.OpenTab("/a", "A");
        var id = _sut.GetTabs()[0].Id;

        var fired = false;
        _sut.OnTabsChanged += () => fired = true;

        _sut.CloseTab(id);

        fired.Should().BeTrue();
    }

    [Fact]
    public void CloseActiveTabShouldActivateNextTab()
    {
        _sut.OpenTab("/a", "A");
        _sut.OpenTab("/b", "B");
        _sut.OpenTab("/c", "C");

        var tabs = _sut.GetTabs();
        _sut.SwitchTab(tabs[1].Id);
        _sut.CloseTab(tabs[1].Id);

        _sut.ActiveTabId.Should().Be(tabs[2].Id);
    }

    [Fact]
    public void CloseLastActiveTabShouldActivatePreviousTab()
    {
        _sut.OpenTab("/a", "A");
        _sut.OpenTab("/b", "B");

        var tabs = _sut.GetTabs();
        _sut.CloseTab(tabs[1].Id);

        _sut.ActiveTabId.Should().Be(tabs[0].Id);
    }

    [Fact]
    public void SwitchTabShouldChangeActiveTab()
    {
        _sut.OpenTab("/a", "A");
        _sut.OpenTab("/b", "B");

        var tabs = _sut.GetTabs();
        _sut.SwitchTab(tabs[0].Id).Should().BeTrue();

        _sut.ActiveTabId.Should().Be(tabs[0].Id);
    }

    [Fact]
    public void SwitchTabShouldReturnFalseForUnknownId()
    {
        _sut.SwitchTab(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void SwitchTabShouldFireOnTabsChanged()
    {
        _sut.OpenTab("/a", "A");
        _sut.OpenTab("/b", "B");

        var fired = false;
        _sut.OnTabsChanged += () => fired = true;

        _sut.SwitchTab(_sut.GetTabs()[0].Id);

        fired.Should().BeTrue();
    }

    [Fact]
    public void UpdateActiveTabShouldChangeUrlAndTitle()
    {
        _sut.OpenTab("/old", "Old");

        _sut.UpdateActiveTab("/new", "New").Should().BeTrue();

        var tab = _sut.GetTabs()[0];
        tab.Url.Should().Be("/new");
        tab.Title.Should().Be("New");
    }

    [Fact]
    public void UpdateActiveTabShouldReturnFalseWhenNoTabs()
    {
        _sut.UpdateActiveTab("/x", "X").Should().BeFalse();
    }

    [Fact]
    public void UpdateActiveTabShouldFireOnTabsChanged()
    {
        _sut.OpenTab("/a", "A");

        var fired = false;
        _sut.OnTabsChanged += () => fired = true;

        _sut.UpdateActiveTab("/b", "B");

        fired.Should().BeTrue();
    }

    [Fact]
    public void UpdateActiveTabShouldPreserveId()
    {
        _sut.OpenTab("/a", "A");
        var originalId = _sut.GetTabs()[0].Id;

        _sut.UpdateActiveTab("/b", "B");

        _sut.GetTabs()[0].Id.Should().Be(originalId);
        _sut.ActiveTabId.Should().Be(originalId);
    }

    [Fact]
    public void CloseTabShouldNotFireOnTabsChangedForUnknownId()
    {
        var fired = false;
        _sut.OnTabsChanged += () => fired = true;

        _sut.CloseTab(Guid.NewGuid());

        fired.Should().BeFalse();
    }

    [Fact]
    public void CloseTabShouldNotFireOnTabsChangedForPinnedTab()
    {
        _sut.OpenTab("/home", "Home", pinned: true);
        var id = _sut.GetTabs()[0].Id;

        var fired = false;
        _sut.OnTabsChanged += () => fired = true;

        _sut.CloseTab(id);

        fired.Should().BeFalse();
    }

    [Fact]
    public void SwitchTabShouldNotFireOnTabsChangedForUnknownId()
    {
        var fired = false;
        _sut.OnTabsChanged += () => fired = true;

        _sut.SwitchTab(Guid.NewGuid());

        fired.Should().BeFalse();
    }

    [Fact]
    public void SwitchTabShouldNotFireOnTabsChangedWhenAlreadyActive()
    {
        _sut.OpenTab("/a", "A");
        var id = _sut.GetTabs()[0].Id;

        var fired = false;
        _sut.OnTabsChanged += () => fired = true;

        _sut.SwitchTab(id);

        fired.Should().BeFalse();
    }

    [Fact]
    public void GetTabsShouldReturnSnapshot()
    {
        _sut.OpenTab("/a", "A");

        var snapshot = _sut.GetTabs();
        _sut.OpenTab("/b", "B");

        snapshot.Should().HaveCount(1);
        _sut.GetTabs().Should().HaveCount(2);
    }

    [Fact]
    public void InitialStateShouldHaveNoTabs()
    {
        _sut.GetTabs().Should().BeEmpty();
        _sut.ActiveTabId.Should().BeNull();
    }

    [Fact]
    public void FindTabByUrlShouldReturnMatchingTab()
    {
        _sut.OpenTab("/clients", "Clients");
        _sut.OpenTab("/quotes", "Quotes");

        var found = _sut.FindTabByUrl("/clients");

        found.Should().NotBeNull();
        found!.Url.Should().Be("/clients");
        found.Title.Should().Be("Clients");
    }

    [Fact]
    public void FindTabByUrlShouldReturnNullWhenNoMatch()
    {
        _sut.OpenTab("/clients", "Clients");

        _sut.FindTabByUrl("/unknown").Should().BeNull();
    }

    [Fact]
    public void FindTabByUrlShouldBeCaseInsensitive()
    {
        _sut.OpenTab("/Clients", "Clients");

        _sut.FindTabByUrl("/clients").Should().NotBeNull();
    }

    [Fact]
    public void FindTabByUrlShouldReturnNullWhenEmpty()
    {
        _sut.FindTabByUrl("/any").Should().BeNull();
    }

    [Fact]
    public void RestoreTabsShouldPopulateEmptyService()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var tabs = new List<TabEntry>
        {
            new(id1, "Clients", "/clients"),
            new(id2, "Quotes", "/quotes"),
        };

        _sut.RestoreTabs(tabs, id2);

        _sut.GetTabs().Should().HaveCount(2);
        _sut.ActiveTabId.Should().Be(id2);
    }

    [Fact]
    public void RestoreTabsShouldTruncateToMaxTabs()
    {
        var sut = new TabManagerService { MaxTabs = 3 };
        var tabs = Enumerable.Range(0, 5)
            .Select(i => new TabEntry(Guid.NewGuid(), $"Tab{i}", $"/t{i}"))
            .ToList();

        sut.RestoreTabs(tabs, tabs[4].Id);

        sut.GetTabs().Should().HaveCount(3);
        sut.ActiveTabId.Should().Be(tabs[0].Id);
    }

    [Fact]
    public void RestoreTabsShouldNoOpWhenTabsAlreadyExist()
    {
        _sut.OpenTab("/existing", "Existing");
        var existingId = _sut.GetTabs()[0].Id;

        var tabs = new List<TabEntry> { new(Guid.NewGuid(), "Other", "/other") };
        _sut.RestoreTabs(tabs, tabs[0].Id);

        _sut.GetTabs().Should().HaveCount(1);
        _sut.GetTabs()[0].Id.Should().Be(existingId);
    }

    [Fact]
    public void RestoreTabsShouldFallbackToFirstTabWhenActiveNotFound()
    {
        var id1 = Guid.NewGuid();
        var tabs = new List<TabEntry> { new(id1, "A", "/a") };

        _sut.RestoreTabs(tabs, Guid.NewGuid());

        _sut.ActiveTabId.Should().Be(id1);
    }

    [Fact]
    public void RestoreTabsShouldFireOnTabsChanged()
    {
        var fired = false;
        _sut.OnTabsChanged += () => fired = true;

        var tabs = new List<TabEntry> { new(Guid.NewGuid(), "A", "/a") };
        _sut.RestoreTabs(tabs, null);

        fired.Should().BeTrue();
    }

    [Fact]
    public void RestoreTabsShouldNotFireWhenEmpty()
    {
        var fired = false;
        _sut.OnTabsChanged += () => fired = true;

        _sut.RestoreTabs([], null);

        fired.Should().BeFalse();
    }

    [Fact]
    public void GetStateShouldReturnCurrentState()
    {
        _sut.OpenTab("/a", "A");
        _sut.OpenTab("/b", "B");

        var (activeId, tabs) = _sut.GetState();

        tabs.Should().HaveCount(2);
        activeId.Should().Be(tabs[1].Id);
    }

    [Fact]
    public void GetStateShouldReturnEmptyWhenNoTabs()
    {
        var (activeId, tabs) = _sut.GetState();

        tabs.Should().BeEmpty();
        activeId.Should().BeNull();
    }

    [Fact]
    public void RestoreAndGetStateShouldRoundTrip()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var original = new List<TabEntry>
        {
            new(id1, "Home", "/", "bi-house", true),
            new(id2, "Clients", "/clients"),
        };

        _sut.RestoreTabs(original, id2);

        var (activeId, tabs) = _sut.GetState();
        activeId.Should().Be(id2);
        tabs.Should().HaveCount(2);
        tabs[0].Should().Be(original[0]);
        tabs[1].Should().Be(original[1]);
    }
}

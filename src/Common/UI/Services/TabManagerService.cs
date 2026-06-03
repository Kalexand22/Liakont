namespace Stratum.Common.UI.Services;

using Stratum.Common.UI.Models;

/// <summary>
/// Circuit-scoped navigation tab manager. Maintains an ordered list of tabs
/// with a configurable maximum (default 10). Runs on the Blazor circuit's
/// single-threaded synchronization context — no locking needed.
/// </summary>
internal sealed class TabManagerService : ITabManagerService
{
    private readonly List<TabEntry> _tabs = [];

    public event Action? OnTabsChanged;

    public Guid? ActiveTabId { get; private set; }

    /// <summary>Maximum number of open tabs. Configurable for testing.</summary>
    internal int MaxTabs { get; init; } = 10;

    public IReadOnlyList<TabEntry> GetTabs()
    {
        return [.. _tabs];
    }

    public bool OpenTab(string url, string title, string? icon = null, bool pinned = false)
    {
        if (_tabs.Count >= MaxTabs)
        {
            return false;
        }

        var entry = new TabEntry(Guid.NewGuid(), title, url, icon, pinned);
        _tabs.Add(entry);
        ActiveTabId = entry.Id;

        OnTabsChanged?.Invoke();
        return true;
    }

    public bool CloseTab(Guid id)
    {
        var index = _tabs.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            return false;
        }

        if (_tabs[index].IsPinned)
        {
            return false;
        }

        _tabs.RemoveAt(index);

        if (ActiveTabId == id)
        {
            ActiveTabId = _tabs.Count > 0
                ? _tabs[Math.Min(index, _tabs.Count - 1)].Id
                : null;
        }

        OnTabsChanged?.Invoke();
        return true;
    }

    public bool SwitchTab(Guid id)
    {
        if (!_tabs.Exists(t => t.Id == id))
        {
            return false;
        }

        if (ActiveTabId == id)
        {
            return true;
        }

        ActiveTabId = id;

        OnTabsChanged?.Invoke();
        return true;
    }

    public TabEntry? FindTabByUrl(string url)
    {
        return _tabs.Find(t => string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase));
    }

    public void RestoreTabs(IReadOnlyList<TabEntry> tabs, Guid? activeTabId)
    {
        if (_tabs.Count > 0)
        {
            return; // Only restore into an empty state
        }

        var toRestore = tabs.Count > MaxTabs ? tabs.Take(MaxTabs).ToList() : tabs;

        foreach (var tab in toRestore)
        {
            _tabs.Add(tab);
        }

        if (activeTabId is not null && _tabs.Exists(t => t.Id == activeTabId.Value))
        {
            ActiveTabId = activeTabId;
        }
        else if (_tabs.Count > 0)
        {
            ActiveTabId = _tabs[0].Id;
        }

        if (_tabs.Count > 0)
        {
            OnTabsChanged?.Invoke();
        }
    }

    public (Guid? ActiveTabId, IReadOnlyList<TabEntry> Tabs) GetState()
    {
        return (ActiveTabId, [.. _tabs]);
    }

    public bool UpdateActiveTab(string url, string title)
    {
        if (ActiveTabId is null)
        {
            return false;
        }

        var index = _tabs.FindIndex(t => t.Id == ActiveTabId);
        if (index < 0)
        {
            return false;
        }

        var current = _tabs[index];
        _tabs[index] = current with { Url = url, Title = title };

        OnTabsChanged?.Invoke();
        return true;
    }
}

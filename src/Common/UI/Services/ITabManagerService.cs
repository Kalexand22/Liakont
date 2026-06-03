namespace Stratum.Common.UI.Services;

using Stratum.Common.UI.Models;

/// <summary>
/// Circuit-scoped service managing navigation tabs.
/// Register via <c>AddCommonUI()</c>. Place a <c>&lt;TabBar /&gt;</c>
/// in the layout to render the tab strip.
/// </summary>
public interface ITabManagerService
{
    /// <summary>Fired when the tab list or active tab changes.</summary>
    event Action? OnTabsChanged;

    /// <summary>The ID of the currently active tab, or <c>null</c> if no tabs are open.</summary>
    Guid? ActiveTabId { get; }

    /// <summary>Returns a snapshot of all open tabs.</summary>
    IReadOnlyList<TabEntry> GetTabs();

    /// <summary>
    /// Opens a new tab with the given URL and title.
    /// Returns <c>false</c> if the maximum tab limit has been reached.
    /// </summary>
    bool OpenTab(string url, string title, string? icon = null, bool pinned = false);

    /// <summary>Closes the tab with the given <paramref name="id"/>. Pinned tabs cannot be closed.</summary>
    bool CloseTab(Guid id);

    /// <summary>Switches the active tab to the one with the given <paramref name="id"/>.</summary>
    bool SwitchTab(Guid id);

    /// <summary>Updates the URL and title of the currently active tab.</summary>
    bool UpdateActiveTab(string url, string title);

    /// <summary>
    /// Finds a tab whose URL matches the given <paramref name="url"/> (case-insensitive, path only).
    /// Returns <c>null</c> if no match.
    /// </summary>
    TabEntry? FindTabByUrl(string url);

    /// <summary>
    /// Restores tabs from a previously saved state. Truncates to the maximum tab limit.
    /// Only valid when no tabs are currently open (i.e., before the first <c>OpenTab</c>).
    /// </summary>
    void RestoreTabs(IReadOnlyList<TabEntry> tabs, Guid? activeTabId);

    /// <summary>
    /// Returns the active tab ID and tab list for serialization. Used by persistence logic.
    /// </summary>
    (Guid? ActiveTabId, IReadOnlyList<TabEntry> Tabs) GetState();
}

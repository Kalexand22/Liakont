namespace Stratum.Common.UI.Models;

/// <summary>
/// Hierarchical scope type for keyboard shortcut resolution.
/// Higher numeric values take priority over lower ones.
/// Resolution order: Modal > Widget > Page > Global.
/// </summary>
public enum ShortcutScopeType
{
    Global = 0,
    Page = 1,
    Widget = 2,
    Modal = 3,
}

namespace Stratum.Common.UI.Models;

/// <summary>
/// Implemented by modules that need hierarchical navigation (sub-menus).
/// Returns a <see cref="NavNode"/> tree directly, bypassing the flat NavSection model.
/// </summary>
public interface INavNodeProvider
{
    NavNode GetNavNode();
}

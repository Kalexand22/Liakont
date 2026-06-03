namespace Stratum.Common.UI.Services;

/// <summary>
/// Resolves a human-readable, localized title for a given URL.
/// Used by <see cref="ITabManagerService"/> and the TabBar component
/// to display tab titles based on the current route.
/// </summary>
public interface ITabTitleProvider
{
    /// <summary>
    /// Returns a localized title for the given relative URL (e.g. "/quotes" → "Devis").
    /// The default implementation humanizes the last URL segment.
    /// Host can register a localized implementation using resource files.
    /// </summary>
    string GetTitle(string url);
}

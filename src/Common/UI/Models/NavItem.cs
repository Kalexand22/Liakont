namespace Stratum.Common.UI.Models;

/// <summary>A single navigation link within a <see cref="NavSection"/>.</summary>
public record NavItem(
    string Label,
    string Href,
    bool ExactMatch = false);

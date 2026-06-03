namespace Stratum.Common.UI.Models;

/// <summary>A navigation section contributed by a module for the ERP sidebar.</summary>
public record NavSection(
    string Title,
    string Icon,
    int Order,
    IReadOnlyList<NavItem> Items);

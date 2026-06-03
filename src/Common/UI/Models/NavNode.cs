namespace Stratum.Common.UI.Models;

/// <summary>
/// Hierarchical navigation node supporting up to 3 levels (Module → Menu → Sub-menu).
/// Nodes with <see cref="Children"/> are expandable containers.
/// Nodes with <see cref="Href"/> are navigable links.
/// A node may have both (clickable section header with children).
/// </summary>
public sealed record NavNode
{
    public required string Label { get; init; }

    public string? Icon { get; init; }

    public string? Href { get; init; }

    public IReadOnlyList<NavNode> Children { get; init; } = [];

    public int Order { get; init; }

    public bool ExactMatch { get; init; }

    /// <summary>Whether this node has child nodes (is a branch in the tree).</summary>
    public bool HasChildren => Children.Count > 0;
}

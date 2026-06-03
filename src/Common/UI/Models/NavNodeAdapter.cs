namespace Stratum.Common.UI.Models;

/// <summary>
/// Converts the legacy flat <see cref="NavSection"/>/<see cref="NavItem"/> model
/// into the hierarchical <see cref="NavNode"/> tree.
/// Each <see cref="NavSection"/> becomes a root-level node with its items as children.
/// </summary>
public static class NavNodeAdapter
{
    /// <summary>
    /// Converts a single <see cref="NavSection"/> to a root-level <see cref="NavNode"/>.
    /// </summary>
    public static NavNode ToNavNode(this NavSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        return new NavNode
        {
            Label = section.Title,
            Icon = section.Icon,
            Order = section.Order,
            Children = section.Items
                .Select(item => new NavNode
                {
                    Label = item.Label,
                    Href = item.Href,
                    ExactMatch = item.ExactMatch,
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Converts multiple <see cref="NavSection"/> instances into an ordered list of <see cref="NavNode"/> trees.
    /// </summary>
    public static IReadOnlyList<NavNode> ToNavNodes(this IEnumerable<NavSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        return sections
            .Select(s => s.ToNavNode())
            .OrderBy(n => n.Order)
            .ToList();
    }

    /// <summary>
    /// Collects sections from all providers and converts to an ordered <see cref="NavNode"/> tree.
    /// </summary>
    public static IReadOnlyList<NavNode> BuildNavTree(this IEnumerable<INavSectionProvider> providers)
    {
        return BuildNavTree(providers, []);
    }

    /// <summary>
    /// Collects sections from flat providers and hierarchical node providers,
    /// merges them into a single ordered <see cref="NavNode"/> tree.
    /// Sections with no items are considered inactive and omitted from the tree.
    /// </summary>
    public static IReadOnlyList<NavNode> BuildNavTree(
        this IEnumerable<INavSectionProvider> sectionProviders,
        IEnumerable<INavNodeProvider> nodeProviders)
    {
        ArgumentNullException.ThrowIfNull(sectionProviders);
        ArgumentNullException.ThrowIfNull(nodeProviders);

        var fromSections = sectionProviders
            .Select(p => p.GetSection())
            .Where(s => s.Items.Count > 0)
            .Select(s => s.ToNavNode());
        var fromNodes = nodeProviders.Select(p => p.GetNavNode());

        return fromSections
            .Concat(fromNodes)
            .OrderBy(n => n.Order)
            .ToList();
    }
}

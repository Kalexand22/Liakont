namespace Stratum.Common.UI.Models;

/// <summary>
/// Internal wrapper for tree-grid items that tracks hierarchy metadata.
/// Used by StratumDataGrid when <c>ChildrenSelector</c> is set.
/// </summary>
/// <typeparam name="TItem">Row data type.</typeparam>
internal sealed class TreeGridNode<TItem>
    where TItem : notnull
{
    public required TItem Item { get; init; }

    public int Level { get; init; }

    public bool HasChildren { get; init; }

    public bool IsExpanded { get; set; }

    public bool IsVisible { get; set; } = true;
}

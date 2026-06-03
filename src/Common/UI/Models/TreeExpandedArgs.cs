namespace Stratum.Common.UI.Models;

/// <summary>
/// Args for tree node expansion. Used for lazy-loading children.
/// Set <see cref="Children"/> in the handler to provide child items dynamically.
/// </summary>
public sealed class TreeExpandedArgs
{
    public TreeExpandedArgs(object? value, string? text)
    {
        Value = value;
        Text = text;
    }

    /// <summary>The data item being expanded.</summary>
    public object? Value { get; }

    /// <summary>The display text of the expanded node.</summary>
    public string? Text { get; }

    /// <summary>
    /// Set this in the handler to provide child items for lazy loading.
    /// The items will be assigned to the <c>ChildrenProperty</c> of the expanded data item.
    /// </summary>
    public System.Collections.IEnumerable? Children { get; set; }
}

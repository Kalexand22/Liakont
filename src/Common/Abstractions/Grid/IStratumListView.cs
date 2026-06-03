namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Common contract for all list view mode components (Table, Card, Kanban, Calendar).
/// Each view renderer implements this interface so that <c>StratumListContainer</c>
/// can delegate data, selection, and events uniformly.
/// </summary>
/// <remarks>
/// This interface uses framework-agnostic types (no Blazor <c>EventCallback</c>)
/// so it can live in Common.Abstractions. Blazor components implementing this
/// bridge to <c>EventCallback</c> via their parameter declarations.
/// </remarks>
/// <typeparam name="TItem">The type of items displayed in the list.</typeparam>
public interface IStratumListView<TItem>
    where TItem : notnull
{
    /// <summary>The items to display.</summary>
    IReadOnlyList<TItem> Data { get; set; }

    /// <summary>Whether the view is in a loading state.</summary>
    bool Loading { get; set; }

    /// <summary>The currently selected items.</summary>
    IReadOnlyList<TItem> SelectedItems { get; set; }

    /// <summary>Accessibility label for the view container.</summary>
    string? AriaLabel { get; set; }
}

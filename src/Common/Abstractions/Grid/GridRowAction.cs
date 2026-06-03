namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Describes a per-row contextual action available via kebab menu on list items.
/// Similar to <see cref="GridAction"/> but the callback receives the row item.
/// </summary>
/// <typeparam name="TItem">The type of row item.</typeparam>
/// <param name="Id">Unique identifier (e.g. "view", "edit", "delete").</param>
/// <param name="Label">Human-readable label (e.g. "Voir", "Modifier", "Supprimer").</param>
/// <param name="Icon">Optional CSS icon class (e.g. "bi-eye", "bi-pencil").</param>
/// <param name="Callback">Async callback invoked with the row item when triggered.</param>
/// <param name="IsEnabled">
/// Optional predicate evaluated per row. When null, the action is always enabled.
/// Receives the row item and returns whether the action is enabled for that item.
/// </param>
/// <param name="RequiresConfirmation">
/// When true, a confirmation dialog is shown before invoking <see cref="Callback"/>.
/// </param>
/// <param name="IsSeparatorBefore">
/// When true, a visual separator is rendered before this action in the menu.
/// Useful to visually separate destructive actions (e.g. delete).
/// </param>
/// <param name="IsQuickAction">
/// When true, the action is rendered as an inline icon button visible on row hover,
/// in addition to appearing in the kebab menu. Typically used for edit/delete.
/// </param>
public sealed record GridRowAction<TItem>(
    string Id,
    string Label,
    string? Icon = null,
    Func<TItem, Task>? Callback = null,
    Func<TItem, bool>? IsEnabled = null,
    bool RequiresConfirmation = false,
    bool IsSeparatorBefore = false,
    bool IsQuickAction = false)
{
    /// <summary>
    /// Evaluates whether the action is enabled for the given item.
    /// </summary>
    public bool EvaluateEnabled(TItem item) => IsEnabled?.Invoke(item) ?? true;
}

namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Describes a contextual action available on a list page (toolbar button or row-level action).
/// </summary>
/// <param name="Id">Unique identifier for the action (e.g. "create-party", "delete").</param>
/// <param name="Label">Human-readable label displayed on the button (e.g. "Nouveau", "Supprimer").</param>
/// <param name="Icon">Optional CSS icon class (e.g. "bi-plus-lg", "bi-trash"). Null for text-only buttons.</param>
/// <param name="Callback">Async callback invoked when the action is triggered.</param>
/// <param name="IsEnabled">
/// Function returning whether the action is currently enabled.
/// Evaluated on each render. Defaults to always enabled.
/// </param>
/// <param name="IsPrimary">
/// When true, the action renders as a primary (filled) button.
/// When false, it renders as a secondary (outlined) button.
/// </param>
/// <param name="RequiresSelection">
/// When true, the action is automatically disabled when no items are selected,
/// regardless of the <see cref="IsEnabled"/> result.
/// </param>
/// <param name="RequiresConfirmation">
/// When true, a confirmation dialog is shown before invoking <see cref="Callback"/>.
/// </param>
public sealed record GridAction(
    string Id,
    string Label,
    string? Icon = null,
    Func<Task>? Callback = null,
    Func<bool>? IsEnabled = null,
    bool IsPrimary = false,
    bool RequiresSelection = false,
    bool RequiresConfirmation = false)
{
    /// <summary>
    /// Evaluates whether the action should be enabled, considering both
    /// <see cref="IsEnabled"/> and <see cref="RequiresSelection"/>.
    /// </summary>
    /// <param name="selectedCount">Number of currently selected items.</param>
    /// <returns>True if the action should be enabled.</returns>
    public bool EvaluateEnabled(int selectedCount)
    {
        if (RequiresSelection && selectedCount == 0)
        {
            return false;
        }

        return IsEnabled?.Invoke() ?? true;
    }
}

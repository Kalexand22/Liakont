namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Groups multiple <see cref="GridAction"/>s under a single dropdown menu button.
/// Renders as a button with a chevron that opens a dropdown list of actions.
/// </summary>
/// <param name="Label">Label displayed on the dropdown toggle button (e.g. "Actions").</param>
/// <param name="Icon">Optional CSS icon class for the toggle button.</param>
/// <param name="Actions">The actions displayed in the dropdown menu.</param>
public sealed record GridActionGroup(
    string Label,
    string? Icon,
    IReadOnlyList<GridAction> Actions);

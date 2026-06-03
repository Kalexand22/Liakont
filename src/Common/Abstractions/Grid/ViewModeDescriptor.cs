namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Describes a view mode's metadata for registration and rendering.
/// </summary>
/// <param name="Kind">The view kind this descriptor represents.</param>
/// <param name="Label">Human-readable label for the view switch button (e.g. "Tableau", "Cartes").</param>
/// <param name="Icon">CSS icon class for the view switch button (e.g. "bi-table", "bi-grid").</param>
/// <param name="RequiredPropertyHint">
/// Optional hint indicating that this view mode requires a specific property mapping
/// to function correctly (e.g. Calendar needs a date field, Kanban needs a category field).
/// Null when no special property is required.
/// </param>
public sealed record ViewModeDescriptor(
    ViewKind Kind,
    string Label,
    string Icon,
    string? RequiredPropertyHint = null);

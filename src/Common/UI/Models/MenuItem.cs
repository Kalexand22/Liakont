namespace Stratum.Common.UI.Models;

/// <summary>
/// Represents an item in a <see cref="Stratum.Common.UI.Components.StratumMenu"/>.
/// </summary>
/// <param name="Text">Display label.</param>
/// <param name="Icon">Optional CSS icon class (e.g. "bi-eye", "bi-pencil"). Rendered as Bootstrap Icons.</param>
/// <param name="Disabled">Whether the item is disabled.</param>
/// <param name="DisabledReason">Tooltip text explaining why the item is disabled.</param>
/// <param name="Separator">If true, renders a visual separator instead of a clickable item.</param>
/// <param name="SubItems">Nested menu items for submenus.</param>
public sealed record MenuItem(
    string Text,
    string? Icon = null,
    bool Disabled = false,
    string? DisabledReason = null,
    bool Separator = false,
    IReadOnlyList<MenuItem>? SubItems = null);

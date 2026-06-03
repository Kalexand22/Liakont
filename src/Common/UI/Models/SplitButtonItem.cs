namespace Stratum.Common.UI.Models;

/// <summary>
/// Represents a secondary action in a <see cref="Stratum.Common.UI.Components.StratumSplitButton"/>.
/// </summary>
/// <param name="Text">Display label.</param>
/// <param name="Icon">Optional icon name (Material Symbols).</param>
/// <param name="Value">Machine value returned on click.</param>
/// <param name="Disabled">Whether the item is disabled.</param>
/// <param name="DisabledReason">Tooltip text explaining why the item is disabled.</param>
public sealed record SplitButtonItem(
    string Text,
    string? Icon = null,
    string? Value = null,
    bool Disabled = false,
    string? DisabledReason = null);

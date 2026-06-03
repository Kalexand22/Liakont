namespace Stratum.Common.UI.Models;

/// <summary>
/// Semantic command registered in <see cref="Services.ICommandRegistry"/>.
/// A command has a stable ID, a human-readable label, and an optional activation condition.
/// It may optionally carry a shortcut hint for display in the help dialog.
/// </summary>
public sealed class CommandDefinition
{
    public CommandDefinition(
        string id,
        string label,
        ShortcutScopeType scope = ShortcutScopeType.Global,
        string? shortcutHint = null,
        Func<bool>? isEnabled = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Id = id;
        Label = label;
        Scope = scope;
        ShortcutHint = shortcutHint;
        IsEnabled = isEnabled;
    }

    /// <summary>Stable identifier (e.g. "save", "open-search", "validate-document").</summary>
    public string Id { get; }

    /// <summary>Human-readable label shown in tooltips and the shortcut help dialog.</summary>
    public string Label { get; }

    /// <summary>Scope at which this command is meaningful.</summary>
    public ShortcutScopeType Scope { get; }

    /// <summary>
    /// Optional display hint for the associated keyboard shortcut (e.g. "Ctrl+S", "/", "?").
    /// Purely informational — does not drive binding resolution.
    /// </summary>
    public string? ShortcutHint { get; }

    /// <summary>
    /// Optional activation guard. When non-null, the command is considered available only
    /// when this delegate returns <c>true</c>. The help dialog respects this flag.
    /// </summary>
    public Func<bool>? IsEnabled { get; }
}

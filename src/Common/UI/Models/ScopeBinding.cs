namespace Stratum.Common.UI.Models;

/// <summary>
/// Associates a keyboard combination with a semantic command within a <see cref="ShortcutScopeType"/>.
/// The optional <see cref="Handler"/> is invoked when the binding is matched.
/// </summary>
public sealed class ScopeBinding
{
    public ScopeBinding(
        string commandId,
        string key,
        bool ctrl = false,
        bool alt = false,
        bool shift = false,
        Func<Task>? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        CommandId = commandId;
        Key = key;
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
        Handler = handler;
    }

    /// <summary>ID of the command to trigger (matches <see cref="CommandDefinition.Id"/>).</summary>
    public string CommandId { get; }

    /// <summary>
    /// Key value as reported by the browser (e.g. "s", "Enter", "Escape", "ArrowDown", "/", "?").
    /// Case-insensitive during resolution.
    /// </summary>
    public string Key { get; }

    public bool Ctrl { get; }

    public bool Alt { get; }

    public bool Shift { get; }

    /// <summary>
    /// Action to invoke when this binding is matched. Required for the binding to be
    /// included in the active JS key map (<see cref="Services.IShortcutService.ComputeActiveBindings"/>).
    /// </summary>
    public Func<Task>? Handler { get; }

    /// <summary>
    /// Canonical key identifier used in the JS active bindings map.
    /// Format: [ctrl+][alt+][shift+]key_lower — e.g. "ctrl+s", "ctrl+shift+enter", "?".
    /// </summary>
    public string KeyId => ComputeKeyId(Key, Ctrl, Alt, Shift);

    /// <summary>Human-readable shortcut display string (e.g. "Ctrl+S", "↓", "?").</summary>
    public string DisplayHint
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Ctrl)
            {
                parts.Add("Ctrl");
            }

            if (Alt)
            {
                parts.Add("Alt");
            }

            if (Shift)
            {
                parts.Add("Shift");
            }

            parts.Add(Key switch
            {
                "ArrowDown" => "↓",
                "ArrowUp" => "↑",
                "ArrowLeft" => "←",
                "ArrowRight" => "→",
                "Enter" => "Enter",
                "Escape" => "Esc",
                " " => "Space",
                _ => Key.ToUpperInvariant(),
            });
            return string.Join("+", parts);
        }
    }

    /// <summary>Computes the canonical key ID from parts.</summary>
    public static string ComputeKeyId(string key, bool ctrl, bool alt, bool shift)
    {
        var sb = new System.Text.StringBuilder();
        if (ctrl)
        {
            sb.Append("ctrl+");
        }

        if (alt)
        {
            sb.Append("alt+");
        }

        if (shift)
        {
            sb.Append("shift+");
        }

        sb.Append(key.ToLowerInvariant());
        return sb.ToString();
    }
}

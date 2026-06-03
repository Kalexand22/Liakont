namespace Stratum.Common.UI.Models;

/// <summary>
/// A group of commands sharing the same <see cref="ShortcutScopeType"/>,
/// used to populate the <c>ShortcutHelpDialog</c>.
/// </summary>
public sealed class CommandGroup
{
    public CommandGroup(
        ShortcutScopeType scopeType,
        string scopeLabel,
        IReadOnlyList<(CommandDefinition Definition, string DisplayHint)> commands)
    {
        ScopeType = scopeType;
        ScopeLabel = scopeLabel;
        Commands = commands;
    }

    public ShortcutScopeType ScopeType { get; }

    public string ScopeLabel { get; }

    /// <summary>Commands in this group with their resolved shortcut display hint.</summary>
    public IReadOnlyList<(CommandDefinition Definition, string DisplayHint)> Commands { get; }
}

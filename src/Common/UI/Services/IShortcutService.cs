namespace Stratum.Common.UI.Services;

using Stratum.Common.UI.Models;

/// <summary>
/// Manages the active scope stack and resolves keyboard shortcuts to semantic commands.
/// Scopes are hierarchical: Modal > Widget > Page > Global.
/// </summary>
public interface IShortcutService
{
    /// <summary>Raised whenever the scope stack changes (push or pop).</summary>
    event Action? ScopeChanged;

    /// <summary>
    /// Pushes a new scope onto the stack.
    /// Called by <c>ShortcutScope</c> on initialization.
    /// </summary>
    void PushScope(string scopeId, ShortcutScopeType scopeType, IReadOnlyList<ScopeBinding> bindings);

    /// <summary>
    /// Removes a scope from the stack by ID.
    /// Called by <c>ShortcutScope</c> on disposal.
    /// </summary>
    void PopScope(string scopeId);

    /// <summary>
    /// Computes the flat key→commandId map that should be active in the browser at the
    /// current moment. Higher-priority scopes overwrite lower-priority ones for the same key.
    /// Only bindings that have a registered <see cref="ScopeBinding.Handler"/> are included.
    /// </summary>
    IReadOnlyDictionary<string, string> ComputeActiveBindings();

    /// <summary>
    /// Executes the handler bound to <paramref name="commandId"/> in the highest-priority
    /// active scope that declares it. Called by <c>GlobalShortcutHandler</c> from JS interop.
    /// </summary>
    Task ExecuteCommandAsync(string commandId);

    /// <summary>
    /// Returns the commands visible in the current scope context, grouped by scope type.
    /// Used by <c>ShortcutHelpDialog</c>.
    /// </summary>
    IReadOnlyList<CommandGroup> GetVisibleCommands(ICommandRegistry registry);
}
